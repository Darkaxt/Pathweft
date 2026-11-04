use crate::Raster;
use serde_json::{json,Value};
use wordcraft_doc::Document;
use wordcraft_layout::{DocLayout,LayoutCache,LayoutOptions};

pub struct Engine { document: Document, layout: DocLayout }

impl Engine {
    pub fn open(bytes:&[u8],extension:&str)->Result<Self,String> {
        let document=match extension {
            "docx"|"docm"=>wordcraft_docx::read(bytes).map_err(|e|e.to_string())?,
            "doc"=>wordcraft_docbin::read(bytes).map_err(|e|e.to_string())?,
            "odt"=>wordcraft_formats::import(extension,bytes).ok_or("Unsupported Word format")??,
            _=>return Err("Unsupported Word format".into()),
        };
        let layout=wordcraft_layout::layout(&document,&mut LayoutCache::new(),&LayoutOptions {hide_deleted:true,..Default::default()});
        if layout.pages.is_empty() {return Err("Craft produced no document pages".into());}
        Ok(Self {document,layout})
    }

    pub fn info(&self)->Value {
        json!({"engine":"WordCraft","kind":"document","parts":[{"name":"Document","pages":self.layout.pages.len()}],"notes":[]})
    }

    pub fn render(&mut self,part:u32,page:u32,width:u32)->Result<Raster,String> {
        if part!=0 {return Err("Invalid document part".into());}
        let page=self.layout.pages.get(page as usize).ok_or("Invalid document page")?;
        if !page.w.is_finite() || !page.h.is_finite() || page.w<=0.0 || page.h<=0.0 {return Err("Invalid document page geometry".into());}
        // Limit both axes while preserving the donor's aspect ratio and layout.
        let scale=(width as f32/page.w).min(4096.0/page.h);
        let raster=wordcraft_render::render_page(&self.document,page,scale,&Default::default());
        Ok(Raster {width:raster.width,height:raster.height,format:0,pixels:raster.pixels})
    }
}
