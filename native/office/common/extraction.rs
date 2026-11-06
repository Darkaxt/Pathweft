// Located text, not an index or a second retained document. Offsets are UTF-8
// byte positions in one native text unit; use only returned continuation tokens.
use serde_json::{json, Value};
pub const MAX_REPLY: usize = 48 * 1024;

pub fn page<T:FnOnce()->String>(units: impl IntoIterator<Item=(Value,T)>, unit:u64, offset:usize, limit:usize, coverage:&str)->Result<Value,String> {
    if !(1024..=MAX_REPLY).contains(&limit) {return Err("Invalid extraction reply limit".into());}
    let mut records=Vec::new(); let mut saw_cursor=false;
    for (index,(location,read_text)) in units.into_iter().enumerate().skip(usize::try_from(unit).map_err(|_|"Invalid extraction cursor")?) {
        saw_cursor=true;
        let text=read_text();
        let start=if index as u64==unit {offset}else{0};
        if start>text.len() || !text.is_char_boundary(start) {return Err("Invalid extraction text offset".into());}
        if text.is_empty() {continue;}
        let occupied=serde_json::to_vec(&records).map_err(|e|e.to_string())?.len();
        let overhead=serde_json::to_vec(&location).map_err(|e|e.to_string())?.len()+coverage.len()+256;
        // Six bytes is JSON's worst ASCII escape expansion. Count UTF-8 bytes,
        // ending at a character boundary, so Unicode cannot exceed this bound.
        let capacity=limit.saturating_sub(occupied+overhead)/6;
        let mut end=start;
        for (relative,ch) in text[start..].char_indices() {
            if relative+ch.len_utf8()>capacity {break;}
            end=start+relative+ch.len_utf8();
        }
        if end==start && start<text.len() {
            if records.is_empty() {return Err("Native text location exceeds extraction reply limit".into());}
            return Ok(json!({"records":records,"next":{"unit":index,"offset":start},"coverage":coverage,"status":"text"}));
        }
        if end>start {records.push(json!({"location":location,"offset":start,"text":&text[start..end]}));}
        if end<text.len() {return Ok(json!({"records":records,"next":{"unit":index,"offset":end},"coverage":coverage,"status":"text"}));}
    }
    if !saw_cursor && (unit!=0||offset!=0) {return Err("Extraction cursor is outside the document".into());}
    Ok(json!({"status":if records.is_empty(){"empty"}else{"text"},"records":records,"next":null,"coverage":coverage}))
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn bounded_continuations_preserve_unicode_and_locations() {
        let original="é 子\n\"\\\u{0000}".repeat(2000);
        let mut cursor=(0,0); let mut collected=String::new();
        loop {
            let result=page([(json!({"paragraph":[2,0,1,3]}),||original.clone())],cursor.0,cursor.1,1024,"paragraphs").unwrap();
            assert!(serde_json::to_vec(&result).unwrap().len()<=1024);
            for record in result["records"].as_array().unwrap() {
                assert_eq!(record["location"]["paragraph"],json!([2,0,1,3]));
                assert_eq!(record["offset"].as_u64().unwrap() as usize,collected.len());
                collected.push_str(record["text"].as_str().unwrap());
            }
            if result["next"].is_null() {break;}
            let next=(result["next"]["unit"].as_u64().unwrap(),result["next"]["offset"].as_u64().unwrap() as usize);
            assert_ne!(cursor,next); cursor=next;
        }
        assert_eq!(original,collected);
    }
    #[test]
    fn invalid_cursor_and_empty_document_are_explicit() {
        assert!(page([(json!({}),||"é".into())],0,1,1024,"text").is_err());
        assert!(page([(json!({}),||"x".into())],9,0,1024,"text").is_err());
        assert_eq!(page::<fn()->String>([],0,0,1024,"text").unwrap()["status"],"empty");
    }
    #[test]
    fn continuation_skips_units_without_copying_their_text() {
        let reads=std::cell::Cell::new(0);
        let units=(0..1000).map(|index|(json!({"cell":index}),{let reads=&reads;move ||{reads.set(reads.get()+1);"value".into()}}));
        let result=page(units,999,0,1024,"cells").unwrap();
        assert_eq!(reads.get(),1);
        assert_eq!(result["records"][0]["location"]["cell"],999);
        assert!(result["next"].is_null());
    }
}
