use std::{fs,path::PathBuf};
use vectorcraft_doc::geom::Rect;
fn main()->Result<(),Box<dyn std::error::Error>> {
    let root=PathBuf::from(std::env::args().nth(1).ok_or("Exact fixture output required")?);fs::create_dir_all(&root)?;
    let svg=r##"<svg xmlns="http://www.w3.org/2000/svg" width="400" height="160" viewBox="0 0 400 160"><rect width="200" height="160" fill="#1234ab"/><rect x="200" width="200" height="160" fill="#dc5220"/><text x="12" y="50" fill="white" font-family="Source Sans 3" font-size="18">Located é 子 😀</text><text x="220" y="90" fill="white" font-size="18">Second board</text></svg>"##;
    let svg=svg.replace("Located é 子 😀",&"Located é 子 😀 ".repeat(100));
    let (mut doc,_)=vectorcraft_svg::import_with_report(&svg)?;
    doc.artboards[0].rect=Rect::new(0.0,0.0,200.0,160.0);doc.artboards[0].name="Blue é".into();
    let mut board=doc.artboards[0].clone();board.id=2;board.name="Orange 子".into();board.rect=Rect::new(200.0,0.0,400.0,160.0);doc.artboards.push(board);
    fs::write(root.join("artboards.vectorcraft"),vectorcraft_format::save_file(&doc))?;
    fs::write(root.join("artboards.drawcraft"),vectorcraft_format::save_file(&doc))?;
    fs::write(root.join("artboards.svg"),&svg)?;
    fs::write(root.join("artboards.svgz"),vectorcraft_svg::compress_bytes(svg.as_bytes()))?;
    fs::write(root.join("linked.svg"),r#"<svg xmlns="http://www.w3.org/2000/svg" width="100" height="100"><image href="file:///C:/Pathweft-do-not-read.png" width="100" height="100"/></svg>"#)?;
    fs::write(root.join("artboards.ai"),vectorcraft_pdf::export(&doc,&Default::default())?)?;
    fs::write(root.join("legacy.ai"),b"%!PS-Adobe-3.0\n%%Creator: Pathweft public-spec fixture\nshowpage\n")?;
    fs::write(root.join("invalid.vectorcraft"),b"not a vector document")?;
    fs::write(root.join("empty.vectorcraft"),vectorcraft_format::save_file(&vectorcraft_doc::Document::new(100.0,100.0)))?;
    println!("Created native, SVG, PDF-compatible AI and rejection fixtures");Ok(())
}
