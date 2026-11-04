use crate::Raster;
use serde_json::{json,Value};
use deckcraft_model::Presentation;
use deckcraft_render::RenderOpts;

pub struct Engine { presentation:Presentation, notes:Vec<String> }

impl Engine {
    pub fn open(bytes:&[u8],extension:&str)->Result<Self,String> {
        if extension!="pptx" {return Err("Unsupported presentation format".into());}
        let (presentation,skipped)=deckcraft_pptx::import_checked(bytes).map_err(|e|e.to_string())?;
        if presentation.slides.is_empty() {return Err("Craft produced no slides".into());}
        let notes=skipped.into_iter().map(|part|format!("Could not import slide part: {part}")).collect();
        Ok(Self {presentation,notes})
    }

    pub fn info(&self)->Value {
        json!({"engine":"DeckCraft","kind":"presentation","parts":[{"name":"Slides","pages":self.presentation.slides.len()}],"notes":self.notes})
    }

    pub fn render(&mut self,part:u32,page:u32,width:u32)->Result<Raster,String> {
        if part!=0 || page as usize>=self.presentation.slides.len() {return Err("Invalid presentation slide".into());}
        let size=&self.presentation.slide_size;
        if !size.width.is_finite() || !size.height.is_finite() || size.width<=0.0 || size.height<=0.0 {return Err("Invalid slide geometry".into());}
        let scale=(f64::from(width)/size.width).min(4096.0/size.height);
        let before=deckcraft_render::panics_caught();
        let raster=deckcraft_render::render_slide(&self.presentation,page as usize,&RenderOpts {scale,threads:0,..Default::default()});
        if deckcraft_render::panics_caught()!=before {return Err("Craft slide rendering panicked".into());}
        Ok(Raster {width:raster.width,height:raster.height,format:0,pixels:raster.pixels})
    }
}
