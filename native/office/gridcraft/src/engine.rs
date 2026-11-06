use crate::Raster;
use serde_json::{json,Value};
use gridcraft_engine::model::Workbook;
use gridcraft_engine::cmd::print::{SheetLayout,PrintOptions,layout_sheet,render_pdf};

// GridCraft's existing non-UI worksheet painter is its print/PDF core. Preserve
// that painter rather than implementing a second cell renderer. Its in-memory
// display list is rasterised inside the same worker using Windows' existing PDF
// API. No CLI, intermediate file or separate PDF viewer process participates.
pub struct Engine { workbook:Workbook, layouts:Vec<SheetLayout>, notes:Vec<String> }

impl Engine {
    pub fn open(bytes:&[u8],extension:&str)->Result<Self,String> {
        let (workbook,report)=match extension {
            "xlsx"|"xlsm"=>gridcraft_xlsx::read_xlsx(bytes),
            "xls"=>gridcraft_xlsx::read_xls(bytes),
            "xlsb"=>gridcraft_xlsx::read_xlsb(bytes),
            "ods"=>gridcraft_xlsx::read_ods(bytes),
            _=>return Err("Unsupported spreadsheet format".into()),
        }.map_err(|e|e.to_string())?;
        if workbook.sheets.is_empty() || workbook.sheets.len()>1000 {return Err("Invalid workbook sheet count".into());}
        let layouts=(0..workbook.sheets.len()).map(|i|layout_sheet(&workbook,i,&PrintOptions::default(),None).ok_or_else(||"Could not lay out worksheet".to_string())).collect::<Result<Vec<_>,_>>()?;
        Ok(Self {workbook,layouts,notes:report.warnings})
    }

    pub fn info(&self)->Value {
        let parts:Vec<_>=self.layouts.iter().map(|layout|json!({"name":self.workbook.sheets[layout.sheet].name,"pages":layout.pages.len()})).collect();
        json!({"engine":"GridCraft","kind":"spreadsheet","parts":parts,"notes":self.notes})
    }

    pub fn extract(&self,unit:u64,offset:usize,limit:usize)->Result<Value,String> {
        let units=self.workbook.sheets.iter().enumerate().flat_map(|(sheet_index,sheet)|{
            sheet.cells.iter().filter(|(_,cell)|cell.has_content()).map(move |(address,cell)|
                (json!({"sheet":sheet_index,"sheetName":sheet.name,"row":address.row,"column":address.col,"cell":address.a1(),"kind":"cachedValue"}),move ||cell.value.display()))
        });
        crate::extraction::page(units,unit,offset,limit,"Imported stored cell values in sheet/row/column order; no formula recalculation, Python, drawing text or OCR")
    }

    pub fn render(&mut self,part:u32,page:u32,_width:u32)->Result<Raster,String> {
        let layout=self.layouts.get(part as usize).ok_or("Invalid worksheet")?;
        if page as usize>=layout.pages.len() {return Err("Invalid worksheet page".into());}
        let (pixels,count)=render_pdf(&self.workbook,std::slice::from_ref(layout),"","");
        if count!=layout.pages.len() {return Err("Worksheet painter returned a different page count".into());}
        Ok(Raster {width:0,height:0,format:1,pixels})
    }
}
