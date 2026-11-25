import Foundation

/// Composite values are calculated by ExifTool. Route familiar editable values
/// to their underlying stored tags; never attempt to write a calculated field.
func editableMetadataTag(_ row: MetadataRow, rows: [MetadataRow]) -> String? {
    if ["File", "ExifTool", "System"].contains(row.group) { return nil }
    guard row.group == "Composite" else { return row.id }
    let aliases = ["LensID": "LensModel", "Aperture": "FNumber", "ShutterSpeed": "ExposureTime"]
    guard let tag = aliases[row.name] else { return nil }
    return rows.first { $0.name == tag && !["Composite", "File", "ExifTool", "System"].contains($0.group) }?.id ?? tag
}

/// Keep a consistent set of common fields available even on stripped exports.
/// Existing group-specific rows take precedence over blank generic fields.
func metadataEditingRows(_ rows: [MetadataRow]) -> [MetadataRow] {
    let common: [(String, String)] = [
        ("Make", "Camera"), ("Model", "Camera"),
        ("LensMake", "Camera"), ("LensModel", "Camera"), ("LensInfo", "Camera"),
        ("Software", "Camera"), ("FNumber", "Capture"), ("ExposureTime", "Capture"),
        ("ISO", "Capture"), ("FocalLength", "Capture"),
        ("DateTimeOriginal", "Dates"), ("CreateDate", "Dates"), ("ModifyDate", "Dates"),
        ("Artist", "Descriptive"), ("Copyright", "Descriptive"),
        ("Title", "Descriptive"), ("Description", "Descriptive"), ("Keywords", "Descriptive"),
        ("GPSLatitude", "GPS"), ("GPSLongitude", "GPS"), ("GPSAltitude", "GPS")
    ]
    let names = Set(rows.filter { editableMetadataTag($0, rows: rows) != nil }.map(\.name))
    let missing = common.filter { !names.contains($0.0) }.map { name, group in
        MetadataRow(id: name, name: name, group: group, original: "—", exported: "—")
    }
    return (rows + missing).sorted { $0.id < $1.id }
}
