// Reproducible native inputs for the Pathweft-owned worker workflow. This
// executable is a development fixture producer and is never packaged.
use photocraft_doc::{Color,ColorMode,Document,Layer,LayerContent,SampleType,Size,TextLayer};
use std::{error::Error,path::PathBuf};

fn main()->Result<(),Box<dyn Error>> {
    let root=PathBuf::from(std::env::args_os().nth(1).ok_or("Fixture output required")?);
    if !root.ends_with("photo-inputs") {return Err("Exact photo-inputs output required".into());}
    std::fs::create_dir_all(&root)?;
    let mut image=Document::with_background("Layer composition",Size::new(128,96),ColorMode::Rgb,SampleType::U8,Color::rgb(0.0,0.0,1.0));
    let mut upper=Layer::new("Half red",LayerContent::Fill(photocraft_doc::Fill::Solid(Color::rgb(1.0,0.0,0.0))));
    upper.opacity=0.5;image.layers.push(upper);
    let mut hidden=Layer::new("Hidden green",LayerContent::Fill(photocraft_doc::Fill::Solid(Color::rgb(0.0,1.0,0.0))));hidden.visible=false;image.layers.push(hidden);
    let native=photocraft_format::save_to_bytes(&image,&Default::default())?;
    std::fs::write(root.join("layers.pcraft"),native)?;
    for extension in ["psd","psb"] {
        let output=photocraft_io::export(&image,extension,&Default::default())?;
        std::fs::write(root.join(format!("layers.{extension}")),output.bytes)?;
    }
    let text=TextLayer{text:"Located é 子 😀 \"quoted\" ".repeat(200),..Default::default()};
    let mut text_layer=Layer::new("Editable Unicode",LayerContent::Text(text));text_layer.visible=false;
    image.layers.push(Layer::group("Text group",vec![text_layer]));
    std::fs::write(root.join("text.pcraft"),photocraft_format::save_to_bytes(&image,&Default::default())?)?;
    println!("Created layered native/PSD/PSB and located Unicode text inputs in {}",root.display());
    Ok(())
}
