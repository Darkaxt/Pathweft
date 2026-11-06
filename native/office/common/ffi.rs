// Pathweft bridge ABI v1. Only the owned preview worker calls this interface,
// serially. Input is immutable bytes; no donor application, command channel,
// user preferences, document writes or macro execution is exposed.
use std::cell::RefCell;
use std::ffi::{c_char, c_void, CString};
use std::panic::{catch_unwind, AssertUnwindSafe};
mod extraction;

#[repr(C)]
pub struct Frame {
    pub width: u32,
    pub height: u32,
    pub format: u32, // 0: premultiplied RGBA8, 1: donor in-memory PDF display list
    pub length: usize,
    pub pixels: *const u8,
}

pub struct Raster {
    pub width: u32,
    pub height: u32,
    pub format: u32,
    pub pixels: Vec<u8>,
}

struct Document {
    engine: engine::Engine,
    info: CString,
    raster: Raster,
    frame: Frame,
    text: CString,
}

thread_local! {
    static ERROR: RefCell<CString> = RefCell::new(CString::default());
}

fn fail(message: String) {
    ERROR.with(|e| *e.borrow_mut() = CString::new(message.replace('\0', " ")).unwrap_or_default());
}

#[unsafe(no_mangle)]
pub extern "C" fn pw_office_abi() -> u32 { 1 }

#[unsafe(no_mangle)]
pub extern "C" fn pw_office_error() -> *const c_char {
    ERROR.with(|e| e.borrow().as_ptr())
}

/// The byte and extension pointers must remain readable for the call. The
/// returned document owns its imported model; it does not retain either pointer.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn pw_office_open(
    bytes: *const u8, length: usize, extension: *const u8, extension_length: usize,
) -> *mut c_void {
    if bytes.is_null() || extension.is_null() || length == 0 || length > 64 * 1024 * 1024 || extension_length > 16 {
        fail("Invalid preview input buffer".into());
        return std::ptr::null_mut();
    }
    let result=catch_unwind(AssertUnwindSafe(|| -> Result<Document,String> {
        let input=unsafe { std::slice::from_raw_parts(bytes,length) };
        let ext=std::str::from_utf8(unsafe { std::slice::from_raw_parts(extension,extension_length) }).map_err(|e| e.to_string())?;
        let engine=engine::Engine::open(input,ext)?;
        let info=CString::new(engine.info().to_string()).map_err(|e|e.to_string())?;
        Ok(Document {engine,info,text:CString::default(),raster:Raster {width:0,height:0,format:0,pixels:Vec::new()},frame:Frame {width:0,height:0,format:0,length:0,pixels:std::ptr::null()}})
    }));
    match result {
        Ok(Ok(document))=>Box::into_raw(Box::new(document)).cast(),
        Ok(Err(error))=>{fail(error);std::ptr::null_mut()},
        Err(_)=>{fail("Craft import panicked".into());std::ptr::null_mut()},
    }
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn pw_office_info(document: *mut c_void) -> *const c_char {
    if document.is_null() { return std::ptr::null(); }
    unsafe { &*document.cast::<Document>() }.info.as_ptr()
}

/// Additive v1 extension. JSON is owned until the next extraction or close;
/// rendering neither invalidates it nor changes the immutable text cursor.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn pw_office_extract(document:*mut c_void,unit:u64,offset:usize,limit:usize)->*const c_char {
    if document.is_null() {fail("Invalid extraction document".into());return std::ptr::null();}
    let doc=unsafe {&mut *document.cast::<Document>()};
    let result=catch_unwind(AssertUnwindSafe(||->Result<CString,String>{
        let bytes=serde_json::to_vec(&doc.engine.extract(unit,offset,limit)?).map_err(|e|e.to_string())?;
        if bytes.len()>limit || bytes.len()>extraction::MAX_REPLY {return Err("Extraction reply exceeds its limit".into());}
        CString::new(bytes).map_err(|e|e.to_string())
    }));
    match result {
        Ok(Ok(text))=>{doc.text=text;doc.text.as_ptr()},
        Ok(Err(error))=>{fail(error);std::ptr::null()},
        Err(_)=>{fail("Craft text extraction panicked".into());std::ptr::null()},
    }
}

/// Returns 0 on success. RGBA pixels are premultiplied and remain owned by the
/// document until the next render or close. The caller must copy before either.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn pw_office_render(document: *mut c_void, part: u32, page: u32, width: u32) -> i32 {
    if document.is_null() || !(64..=4096).contains(&width) {
        fail("Invalid preview render request".into());return -1;
    }
    let doc=unsafe { &mut *document.cast::<Document>() };
    match catch_unwind(AssertUnwindSafe(||doc.engine.render(part,page,width))) {
        Ok(Ok(raster))=>{
            let expected=u64::from(raster.width)*u64::from(raster.height)*4;
            let valid=match raster.format {
                0=>expected>0 && expected<=64*1024*1024 && expected==raster.pixels.len() as u64,
                1=>raster.pixels.starts_with(b"%PDF-") && raster.pixels.len()<=64*1024*1024,
                _=>false,
            };
            if !valid {
                fail("Craft returned an invalid raster".into());return -1;
            }
            doc.raster=raster;
            doc.frame=Frame {width:doc.raster.width,height:doc.raster.height,format:doc.raster.format,length:doc.raster.pixels.len(),pixels:doc.raster.pixels.as_ptr()};
            0
        },
        Ok(Err(error))=>{fail(error);-1},
        Err(_)=>{fail("Craft rendering panicked".into());-1},
    }
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn pw_office_frame(document: *mut c_void) -> *const Frame {
    if document.is_null() {return std::ptr::null();}
    &unsafe { &*document.cast::<Document>() }.frame
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn pw_office_close(document: *mut c_void) {
    if !document.is_null() {
        // Native process-ending faults are handled by the parent worker owner.
        let _=catch_unwind(AssertUnwindSafe(||drop(unsafe {Box::from_raw(document.cast::<Document>())})));
    }
}
