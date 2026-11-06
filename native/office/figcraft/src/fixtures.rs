use std::{fs,path::PathBuf};
use figcraft_doc::{Document,NodeKind,TextData,color::{Color,Paint}};
fn main()->Result<(),Box<dyn std::error::Error>> {
    let root=PathBuf::from(std::env::args().nth(1).ok_or("Exact fixture output required")?);fs::create_dir_all(&root)?;
    let mut document=Document::new("Pathweft frames");let page=document.pages[0];
    for (index,name,color) in [(0,"Blue é",Color{r:18.0/255.0,g:52.0/255.0,b:171.0/255.0,a:1.0}),(1,"Orange 子",Color{r:220.0/255.0,g:82.0/255.0,b:32.0/255.0,a:1.0})] {
        let frame=document.create(page,NodeKind::Frame,name,f64::from(index)*230.0,0.0,200.0,160.0)?;
        document.node_mut(frame).ok_or("Missing fixture frame")?.fills=vec![Paint::solid(color)];
        let mut text=TextData{characters:if index==0{"Located é 子 😀 \"quoted\"\n".repeat(100)}else{"Second frame".into()},..Default::default()};
        text.style.fills=Some(vec![Paint::solid(Color::WHITE)]);
        document.create(frame,NodeKind::Text(text),"Located text",12.0,25.0,180.0,100.0)?;
    }
    let hidden=document.create(page,NodeKind::Text(TextData{characters:"Hidden note".into(),..Default::default()}),"Hidden type",0.0,200.0,100.0,30.0)?;
    document.node_mut(hidden).ok_or("Missing hidden fixture")?.visible=false;
    fs::write(root.join("frames.figcraft"),figcraft_format::save_bytes(&document,None)?)?;
    let (fig,notes)=figcraft_format::fig::export(&document,None)?;fs::write(root.join("frames.fig"),fig)?;
    fs::write(root.join("fig-export-notes.json"),serde_json::to_vec_pretty(&notes)?)?;
    fs::write(root.join("empty.figcraft"),figcraft_format::save_bytes(&Document::new("Empty"),None)?)?;
    fs::write(root.join("invalid.figcraft"),b"not a design")?;fs::write(root.join("invalid.fig"),b"fig-invalid")?;
    println!("Created native and binary Figma frame/text fixtures");Ok(())
}
