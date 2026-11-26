import AppKit
import SwiftUI
import QuickLookThumbnailing

struct Match: Identifiable { let id: String; let original: URL?; let exported: URL; let status: String }
struct MetadataRow: Identifiable { let id: String; let name: String; let group: String; let original: String; let exported: String; var mixed = false; var changed: Bool { mixed || original != exported } }
final class Library: ObservableObject {
 @Published var mode: WorkspaceMode = .metadata {
  didSet {
   guard oldValue != mode else { return }
   let shouldEdit = mode == .metadata
   if editing != shouldEdit { editing = shouldEdit }
   if !message.isEmpty { message = "" }
   inspect()
   onModeChanged?(mode)
  }
 }
 var onModeChanged: ((WorkspaceMode) -> Void)?
 var mediaItemsVersion = 0
 var pendingVersion = 0
 @Published var mediaItems = [MediaItem]() { didSet { mediaItemsVersion += 1 } }
 @Published var mediaSelection = Set<String>() { didSet { if mode != .transfer && oldValue != mediaSelection { inspect() } } }
 @Published var mediaLoading = false
 @Published var loadingProgress: Double? = nil
 var displayedMediaOrder: [String] = []
 @Published var locationDrafts = [String: MapCoordinate]() { didSet { pendingVersion += 1 } }
 @Published var workflowDrafts = [String: [String]]() { didSet { pendingVersion += 1 } }
 func hasPending(_ path: String) -> Bool {
  drafts[path]?.isEmpty == false || locationDrafts[path] != nil || workflowDrafts[path] != nil
 }
 var pendingPaths: Set<String> {
  Set(drafts.filter { !$0.value.isEmpty }.keys).union(locationDrafts.keys).union(workflowDrafts.keys)
 }
 @Published var presets = [Preset]()
 @Published var showWorkflows = false
 @Published var showSettings = false
 @Published var drafts = [String: [String: String]]() { didSet { pendingVersion += 1 } }
 @Published var editOriginal = false
 @Published var editing = true
 @Published var newTag = ""
 @Published var newValue = ""
 @Published var defaultExports: URL?
 let workflowFile: URL
 @Published var requestNewWorkflow = false
 @Published var saveWorkflowRequest = UUID()
 @Published var lastMapPoint: MapCoordinate?
 @Published var mapFocusRequest: LocationPin?
 @Published var requestSaveFavourite = false
 @Published var favouriteLocationsVersion = UUID()
 var mediaLoadVersion = 0
 init(workflowFile: URL? = nil) {
  self.workflowFile = workflowFile ?? FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0].appendingPathComponent("Metadata Transfer/Workflows.json")
  loadPresets()
  configureExifTool()
  if let data=UserDefaults.standard.data(forKey:"DefaultExportsBookmark") { var stale=false; defaultExports=try? URL(resolvingBookmarkData:data,options:[],relativeTo:nil,bookmarkDataIsStale:&stale); destination=defaultExports }
 }
 func saveDefaultExports() {
  if let destination { storeDefaultExports(destination) }
 }
 func storeDefaultExports(_ folder: URL) {
  do {
   let bookmark = try folder.bookmarkData(options: [], includingResourceValuesForKeys: nil, relativeTo: nil)
   UserDefaults.standard.set(bookmark, forKey: "DefaultExportsBookmark")
   defaultExports = folder
  } catch {
   NSAlert(error: error).runModal()
  }
 }
 func clearDefaultExports(){ UserDefaults.standard.removeObject(forKey:"DefaultExportsBookmark"); defaultExports=nil }
 @Published var source: URL?
 @Published var destination: URL?
 @Published var files: [Match] = []
 @Published var unmatchedOriginals: [URL] = []
 @Published var sourceItems = [URL]()
 @Published var exportItems = [URL]()
 private var scanVersion = 0

 func receive(_ urls: [URL], originals: Bool) -> Bool {
  guard !busy else { return false }
  let resolved = urls.filter(\.isFileURL).map { $0.standardizedFileURL.resolvingSymlinksInPath() }
  let folders = resolved.filter { (try? $0.resourceValues(forKeys: [.isDirectoryKey]).isDirectory) == true }
  if resolved.count == 1, let folder = folders.first {
   if originals { sourceItems=[]; source=folder } else { exportItems=[]; destination=folder }
   scan()
   return true
  }
  if !folders.isEmpty {
   busy = true
   DispatchQueue.global(qos: .userInitiated).async {
    let expanded = resolved.flatMap { url in folders.contains(url) ? self.list(url) : [url] }
    DispatchQueue.main.async {
     self.busy = false
     _ = self.receive(expanded, originals: originals)
    }
   }
   return true
  }
  let regular = resolved.filter { url in
   guard kind(url) != nil else { return false }
   return (try? url.resourceValues(forKeys: [.isRegularFileKey]).isRegularFile) == true
  }
  guard !regular.isEmpty else { return false }
  if originals {
   source = nil
   sourceItems = Array(Set(sourceItems + regular))
  } else {
   destination = nil
   exportItems = Array(Set(exportItems + regular))
  }
  scan()
  return true
 }
 @Published var originalSelection = Set<String>()
 @Published var exportSelection = Set<String>()
 @Published var activeOriginals = false

 var selectedTargets: [URL] {
  if mode != .transfer { return mediaItems.filter { mediaSelection.contains($0.id) }.map(\.url) }
  if activeOriginals {
   let paired = files.filter { originalSelection.contains($0.id) }.compactMap(\.original)
   let unpaired = unmatchedOriginals.filter { originalSelection.contains($0.path) }
   return Array(Set(paired + unpaired)).sorted { $0.path < $1.path }
  }
  return files.filter { exportSelection.contains($0.id) }.map(\.exported)
 }

 func updateSelection(_ proposed: Set<String>, originals: Bool) {
  let ids = proposed.intersection(selectableIDs(originals: originals))
  let previous = originals ? originalSelection : exportSelection
  activeOriginals = originals
  editOriginal = originals
  if originals { originalSelection = ids; exportSelection = [] }
  else { exportSelection = ids; originalSelection = [] }
  let newlySelected = ids.subtracting(previous).sorted().first
  selection = newlySelected ?? (selection.flatMap { ids.contains($0) ? $0 : nil }) ?? ids.sorted().first
 }

 @Published var selection: String? { didSet { inspect() } }
 @Published var metadata: [MetadataRow] = []
 @Published var locationPins = [LocationPin]()
 private var inspectionVersion = 0
 @Published var showInspector = UserDefaults.standard.object(forKey: "ShowPreviewOnLaunch") as? Bool ?? true {
  didSet { if showInspector && !oldValue && mode == .metadata { inspect() } }
 }
 @Published var changesOnly = true
 @Published var busy = false
 @Published var reading = false
 @Published var message = ""
 @Published var report: String?
 @Published var exif: String?
 @Published var customExifToolPath: String?
 @Published var exifToolVersion: String?
 @Published var exifToolError: String?
 private let exifLock = NSLock()
 private var activeExifPath: String?
 func setActiveExifPath(_ path: String?) {
  exifLock.lock()
  activeExifPath = path
  exifLock.unlock()
 }
 func currentExifPath() -> String? {
  exifLock.lock()
  defer { exifLock.unlock() }
  return activeExifPath
 }
 let image = Set("3fr arq arw avif bmp cr2 cr3 crw dcr dng erf fff gif gpr heic heif hif iiq insp j2k jp2 jpeg jpg jxl mef mos mrw nef nrw orf pef png psb psd raf raw rw2 rwl sr2 srf srw tif tiff webp x3f".split(separator:" ").map(String.init))
 let video = Set("3g2 3gp 3gp2 3gpp avi crm dv f4v m2ts m4v mkv mov mp4 mpeg mpg mts mxf qt webm wmv".split(separator:" ").map(String.init))
 var selected: Match? {
  if mode != .transfer { return selectedTargets.first.map { Match(id: $0.path, original: nil, exported: $0, status: "Media") } }
  if let pair = files.first(where: { $0.id == selection }) { return pair }
  guard let url = unmatchedOriginals.first(where: { $0.path == selection }) else { return nil }
  return Match(id: url.path, original: url, exported: url, status: "Original only")
 }
 var matched: Int { files.filter { $0.original != nil }.count }
 func kind(_ url: URL) -> String? { image.contains(url.pathExtension.lowercased()) ? "image" : video.contains(url.pathExtension.lowercased()) ? "video" : nil }
 func choose(_ originals: Bool) {
  let panel = NSOpenPanel(); panel.canChooseFiles=false; panel.canChooseDirectories=true; panel.allowsMultipleSelection=false
  panel.message = originals ? "Choose the folder containing your original files." : "Choose the folder containing your exported files."
  panel.prompt = "Choose Folder"
  panel.directoryURL = originals ? source : destination ?? defaultExports
  if panel.runModal() == .OK, let url=panel.url { if originals { sourceItems=[]; source=url } else { exportItems=[]; destination=url }; scan() }
 }
 func list(_ folder: URL) -> [URL] {
  guard let iterator=FileManager.default.enumerator(at:folder,includingPropertiesForKeys:[.isRegularFileKey,.isSymbolicLinkKey],options:[.skipsHiddenFiles,.skipsPackageDescendants]) else{return []}
  return iterator.compactMap { $0 as? URL }.filter { url in guard kind(url) != nil, let v=try? url.resourceValues(forKeys:[.isRegularFileKey,.isSymbolicLinkKey]) else{return false}; return v.isRegularFile == true && v.isSymbolicLink != true }.sorted { $0.path.localizedStandardCompare($1.path) == .orderedAscending }
 }
 func scan() {
  scanVersion += 1
  let version = scanVersion
  let source = self.source, destination = self.destination
  let sourceItems = self.sourceItems, exportItems = self.exportItems
  busy=true; message="Comparing files…"
  DispatchQueue.global(qos:.userInitiated).async {
   let sources = (source.map(self.list) ?? sourceItems).sorted { $0.path.localizedStandardCompare($1.path) == .orderedAscending }
   let exports = (destination.map(self.list) ?? exportItems).sorted { $0.path.localizedStandardCompare($1.path) == .orderedAscending }
   let sourcePaths = Set(sources.map { $0.standardizedFileURL.resolvingSymlinksInPath().path })
   var index=[String:[URL]]()
   func key(_ url:URL)->String { (self.kind(url) ?? "")+":"+url.deletingPathExtension().lastPathComponent.lowercased() }
   for url in sources { index[key(url),default:[]].append(url) }
   let matches=exports.map { url -> Match in
    if sourcePaths.contains(url.standardizedFileURL.resolvingSymlinksInPath().path) { return Match(id:url.path,original:nil,exported:url,status:"Same file") }
    let k=key(url); var candidates=index[k] ?? []
    if candidates.isEmpty && k.hasSuffix("-hdr") { candidates=index[String(k.dropLast(4))] ?? [] }
    return Match(id:url.path,original:candidates.count == 1 ? candidates[0]:nil,exported:url,status:candidates.count == 1 ? "Matched" : candidates.isEmpty ? "No match":"Ambiguous")
   }
   let paired = Set(matches.compactMap(\.original))
   DispatchQueue.main.async {
    guard self.scanVersion == version else { return }
    self.unmatchedOriginals = sources.filter { !paired.contains($0) }
    self.files = matches
    self.originalSelection.formIntersection(self.selectableIDs(originals: true))
    self.exportSelection.formIntersection(self.selectableIDs(originals: false))
    let remaining = self.activeOriginals ? self.originalSelection : self.exportSelection
    if let current = self.selection, remaining.contains(current) {
     self.inspect()
    } else if let retained = remaining.sorted().first {
     self.selection = retained
    } else if let first = matches.first {
     self.updateSelection([first.id], originals: false)
    } else if let first = self.unmatchedOriginals.first {
     self.updateSelection([first.path], originals: true)
    } else {
     self.selection = nil
    }
    self.busy=false
    self.message=exports.isEmpty ? "Drop exports or choose an exports folder." : ""
   }
  }
 }
 func run(_ arguments:[String])->(Int32,String) {
  guard let exif=currentExifPath() else{return (1,"ExifTool is not available. Check Settings to restore or choose ExifTool.")}; let process=Process(); process.executableURL=URL(fileURLWithPath:exif); process.arguments=arguments; let pipe=Pipe(); process.standardInput=FileHandle.nullDevice; process.standardOutput=pipe; process.standardError=pipe
  do { try process.run(); let data=pipe.fileHandleForReading.readDataToEndOfFile(); process.waitUntilExit(); return (process.terminationStatus,String(data:data,encoding:.utf8) ?? "") } catch{return (1,error.localizedDescription)}
 }
 func runJSON(_ arguments:[String]) -> Data? {
  guard let exif = currentExifPath() else { return nil }
  let process = Process()
  process.executableURL = URL(fileURLWithPath: exif)
  process.arguments = arguments
  let stdout = Pipe()
  process.standardInput = FileHandle.nullDevice
  process.standardOutput = stdout
  process.standardError = FileHandle.nullDevice
  do {
   try process.run()
   let data = stdout.fileHandleForReading.readDataToEndOfFile()
   process.waitUntilExit()
   return data.isEmpty ? nil : data
  } catch {
   return nil
  }
 }
 func read(_ url:URL)->[String:Any] {
  guard let data = runJSON(["-j","-G1","-s","-q","-q","-c","%+.7f",url.path]),
        let rows = (try? JSONSerialization.jsonObject(with: data)) as? [[String:Any]] else { return [:] }
  return rows.first ?? [:]
 }
 func readBatch(_ urls:[URL])->[String:[String:Any]] {
  guard !urls.isEmpty else { return [:] }
  if let data = runJSON(["-j","-G1","-s","-q","-q","-c","%+.7f"] + urls.map(\.path)),
     let rows = (try? JSONSerialization.jsonObject(with: data)) as? [[String:Any]] {
   var result = [String:[String:Any]]()
   result.reserveCapacity(rows.count * 2)
   for row in rows {
    if let source = row["SourceFile"] as? String {
     result[source] = row
     let normalized = URL(fileURLWithPath: source).standardizedFileURL.resolvingSymlinksInPath().path
     result[normalized] = row
    }
   }
   return result
  }
  guard urls.count > 1 else { return [:] }
  var fallback = [String:[String:Any]]()
  for url in urls {
   let tags = read(url)
   if !tags.isEmpty {
    fallback[url.path] = tags
    fallback[url.standardizedFileURL.resolvingSymlinksInPath().path] = tags
   }
  }
  return fallback
 }
 func inspect() {
  inspectionVersion += 1
  let version = inspectionVersion
  if mode != .transfer {
   let items = mediaItems.filter { mediaSelection.contains($0.id) }
   let pins = items.compactMap(\.pin)
   locationPins = pins
   guard mode == .metadata && showInspector else {
    if !metadata.isEmpty { metadata = [] }
    if reading { reading = false }
    return
   }
   let tagDocs = items.map(\.tags)
   if tagDocs.count <= 1 {
    metadata = aggregateMetadata(tagDocs)
    reading = false
   } else {
    reading = true
    DispatchQueue.global(qos: .userInitiated).async {
     let rows = aggregateMetadata(tagDocs)
     DispatchQueue.main.async {
      guard self.inspectionVersion == version else { return }
      self.metadata = rows
      self.reading = false
     }
    }
   }
   return
  }
  metadata=[]; locationPins=[]
  guard let pair=selected else { reading=false; return }
  let urls=selectedTargets
  let selectedIDs = activeOriginals ? originalSelection : exportSelection
  let referenceURLs = Array(Set(files.filter { selectedIDs.contains($0.id) }.flatMap { [$0.original, Optional($0.exported)].compactMap { $0 } }).subtracting(Set(urls)))
  reading=true
  DispatchQueue.global(qos:.userInitiated).async {
   let rows: [MetadataRow]
   if urls.count > 1 {
    rows = aggregateMetadata(urls.map(self.read), referenceDocuments: referenceURLs.map(self.read))
   } else {
    let a=pair.original.map{self.read($0)} ?? [:], b=self.read(pair.exported)
    let otherRows=Set(a.keys).union(b.keys).filter { cameraTagName($0) == nil && !$0.hasPrefix("System:") && !$0.hasPrefix("ExifTool:") && $0 != "SourceFile" }.sorted().map { key in
     let parts=key.split(separator:":").map(String.init)
     return MetadataRow(id:key,name:parts.last ?? key,group:parts.first ?? "",original:a[key].map{String(describing:$0)} ?? "—",exported:b[key].map{String(describing:$0)} ?? "—")
    }
    rows = (otherRows + comparedCameraRows(original:a, exported:b)).sorted { $0.id < $1.id }
   }
   let pins=(urls.isEmpty ? [pair.exported] : urls).compactMap(self.readLocation)
   DispatchQueue.main.async { guard self.inspectionVersion == version else { return }; self.metadata=rows; self.locationPins=pins; self.reading=false }
  }
 }
 private func selectableIDs(originals: Bool) -> [String] {
  if originals { return files.filter { $0.original != nil }.map(\.id) + unmatchedOriginals.map(\.path) }
  return files.map(\.id)
 }
 func navigationTarget(_ offset: Int) -> String? {
  if mode != .transfer {
   let ids = displayedMediaOrder.isEmpty ? mediaItems.map(\.id) : displayedMediaOrder
   guard let first = ids.firstIndex(where: mediaSelection.contains) else { return offset < 0 ? ids.last : ids.first }
   return ids.indices.contains(first + offset) ? ids[first + offset] : nil
  }
  let ids = selectableIDs(originals: activeOriginals)
  guard let selection, let index = ids.firstIndex(of: selection) else {
   return offset < 0 ? ids.last : ids.first
  }
  let next = index + offset
  return ids.indices.contains(next) ? ids[next] : nil
 }
 func navigate(_ offset: Int) {
  guard !busy, let target = navigationTarget(offset) else { return }
  if mode == .transfer { updateSelection([target], originals: activeOriginals) } else { mediaSelection = [target] }
 }
 func transfer() {
  let pairs=files.filter{$0.original != nil}; guard mode == .transfer, !busy, !pairs.isEmpty else{return}
  let alert=NSAlert(); alert.messageText="Copy metadata to \(pairs.count) exports?"; alert.informativeText="A backup will be saved beside each modified export. ExifTool copies writable metadata; support depends on the destination format."; alert.addButton(withTitle:"Copy Metadata"); alert.addButton(withTitle:"Cancel"); guard alert.runModal() == .alertFirstButtonReturn else{return}
  busy=true; message="Copying metadata…"
  DispatchQueue.global(qos:.userInitiated).async {
   var log="", failed=0
   for (index,pair) in pairs.enumerated() {
    let (code,out)=self.run(["-P","-TagsFromFile",pair.original!.path,"-all:all",pair.exported.path]); if code != 0 {failed += 1}; log += "\(pair.exported.path)\n\(out)\n"
    DispatchQueue.main.async { self.message="Copying metadata · \(index+1) of \(pairs.count)" }
   }
   let result=log, failures=failed
   DispatchQueue.main.async {self.busy=false; self.message="\(pairs.count-failures) completed · \(failures) failed"; self.report=result; self.inspect()}
  }
 }
}
final class ThumbnailStore {
 static let shared = ThumbnailStore(size: NSSize(width: 72, height: 72), byteLimit: 24 * 1024 * 1024, count: 256)
 static let preview = ThumbnailStore(size: NSSize(width: 480, height: 320), byteLimit: 12 * 1024 * 1024, count: 8)
 let cache = NSCache<NSURL, NSImage>()
 private let size: NSSize
 private var waiting: [URL: [(NSImage) -> Void]] = [:]
 private var queue: [URL] = []
 private var active = 0

 private init(size: NSSize, byteLimit: Int, count: Int) {
  self.size = size
  cache.totalCostLimit = byteLimit
  cache.countLimit = count
 }

 func load(_ url: URL, _ done: @escaping (NSImage) -> Void) {
  if let image = cache.object(forKey: url as NSURL) { done(image); return }
  if waiting[url] != nil { waiting[url]?.append(done); return }
  waiting[url] = [done]
  queue.append(url)
  startNext()
 }

 private func startNext() {
  while active < 4, !queue.isEmpty {
   let url = queue.removeFirst()
   active += 1
   let request = QLThumbnailGenerator.Request(fileAt: url, size: size, scale: 2, representationTypes: .all)
   QLThumbnailGenerator.shared.generateBestRepresentation(for: request) { result, _ in
    DispatchQueue.main.async {
     let image = result?.nsImage ?? NSWorkspace.shared.icon(forFile: url.path)
     if let bitmap = result?.cgImage {
      self.cache.setObject(image, forKey: url as NSURL, cost: bitmap.bytesPerRow * bitmap.height)
     }
     let callbacks = self.waiting.removeValue(forKey: url) ?? []
     self.active -= 1
     callbacks.forEach { $0(image) }
     self.startNext()
    }
   }
  }
 }
}
struct Thumbnail: View {
 let url: URL?
 var quickLookRow: QuickLookRow? = nil
 var contentMode: ContentMode = .fit
 var cornerRadius: CGFloat = 5
 var largePreview = false
 @State private var image: NSImage?
 @Environment(\.accessibilityReduceMotion) private var reduceMotion

 var body: some View {
  ZStack {
   if let image {
    Image(nsImage: image)
     .resizable()
     .aspectRatio(contentMode: contentMode)
     .background {
      if let quickLookRow, let url {
       QuickLookThumbnail(row: quickLookRow, url: url, image: image)
      }
     }
     .transition(.opacity)
   } else {
    Image(systemName:url == nil ? "doc.badge.questionmark" : "photo")
     .foregroundStyle(.tertiary)
   }
  }
  .clipShape(RoundedRectangle(cornerRadius:cornerRadius,style:.continuous))
  .task(id:url) {
   image = nil
   guard let url else { return }
   let store = largePreview ? ThumbnailStore.preview : ThumbnailStore.shared
   if let cached = store.cache.object(forKey:url as NSURL) {
    image = cached
    return
   }
   let loaded: NSImage = await withCheckedContinuation { continuation in
    store.load(url) { continuation.resume(returning:$0) }
   }
   guard !Task.isCancelled else { return }
   withAnimation(reduceMotion ? nil : .easeOut(duration:0.14)) { image = loaded }
  }
 }
}
struct Vibrancy: NSViewRepresentable {
 var material: NSVisualEffectView.Material = .sidebar
 var blendingMode: NSVisualEffectView.BlendingMode = .behindWindow
 func makeNSView(context:Context)->NSVisualEffectView { let view=NSVisualEffectView(); view.material = material; view.blendingMode = blendingMode; view.state = .followsWindowActiveState; return view }
 func updateNSView(_ view:NSVisualEffectView,context:Context){ view.material = material; view.blendingMode = blendingMode }
}
struct FilePane: View {
 @ObservedObject var library:Library; let originals:Bool
 @State private var hovering = false
 @Environment(\.accessibilityReduceMotion) private var reduceMotion
 @Environment(\.joongaAccent) private var accent
 var itemCount:Int { originals ? library.sourceItems.count : library.exportItems.count }
 var hasFiles: Bool {
  originals ? !library.unmatchedOriginals.isEmpty || library.files.contains { $0.original != nil } : !library.files.isEmpty
 }
 var folder:URL? { originals ? library.source:library.destination }
 var body: some View {
  VStack(spacing:0) {
   HStack(spacing: 8) {
    let action = folder == nil ? "Choose \(originals ? "Originals" : "Exports") Folder" : "Open \(originals ? "Originals" : "Exports") in Finder"
    Button {
     library.openOrChooseFolder(originals: originals)
    } label: {
     Image(systemName: "folder")
      .symbolRenderingMode(.hierarchical)
      .frame(width: 24, height: 22)
    }
    .buttonStyle(.borderless)
    .tint(.primary)
    .controlSize(.small)
    .accessibilityLabel(action)
    .help(folder.map { action + "\n" + $0.path } ?? action)
    .disabled(folder == nil && library.busy)

    VStack(alignment: .leading, spacing: 2) {
     Text(originals ? "Originals" : "Exports").font(.system(size: 12, weight: .semibold))
     Text(folder?.path ?? (itemCount > 0 ? "\(itemCount) individual files" : "Drop files or choose a folder"))
      .font(.system(size: 10))
      .foregroundStyle(.secondary)
      .lineLimit(1)
      .truncationMode(.middle)
      .help(folder?.path ?? "")
    }
    Spacer(minLength: 4)
    Menu {
     Button("Choose Folder…") { library.choose(originals) }
     if let folder {
      Button("Open in Finder") { library.openFolder(folder) }
      Button("Copy Folder Path") {
       NSPasteboard.general.clearContents()
       NSPasteboard.general.setString(folder.path, forType: .string)
      }
     }
     if !originals {
      Divider()
      Button("Use as Default Exports Folder") { library.saveDefaultExports() }.disabled(folder == nil)
      if library.defaultExports != nil {
       Button("Clear Default Folder") { library.clearDefaultExports() }
      }
     }
    } label: {
     Image(systemName: !originals && library.defaultExports == folder && folder != nil ? "pin.circle.fill" : "ellipsis.circle")
    }
    .menuStyle(.borderlessButton)
    .tint(.primary)
    .menuIndicator(.hidden)
    .fixedSize()
    .help("Folder options")
    .disabled(library.busy)
   }.padding(.horizontal, 16).frame(height: 44).background(Color.black.opacity(0.07))
   Divider()
   ZStack {
    Canvas { context, size in
     let rowCount = hasFiles ? library.files.count + (originals ? library.unmatchedOriginals.count : 0) : 0
     let firstEmptyStripe = rowCount + rowCount % 2
     for index in stride(from: firstEmptyStripe, to: Int(ceil(size.height / 46)), by: 2) {
      let rect = CGRect(x:8, y:CGFloat(index)*46, width:max(0,size.width-16), height:46)
      context.fill(Path(roundedRect:rect,cornerRadius:6),with:.color(.black.opacity(0.07)))
     }
    }.allowsHitTesting(false).accessibilityHidden(true)
   if !hasFiles {
    VStack(spacing:10) {
     Image(systemName:originals ? "folder" : "tray")
      .font(.system(size:26,weight:.light)).foregroundStyle(.secondary)
     Text(folder != nil ? "No supported media in this folder" : originals ? "Drop original files here" : "Drop exported files here")
      .font(.system(size:13,weight:.medium))
     Button("Choose Folder…") { library.choose(originals) }.controlSize(.small).disabled(library.busy)
    }.padding(.vertical,18).padding(.horizontal,24)

   } else {
    TransferFileTable(library: library, originals: originals)
   }
   }
  }.overlay {
   if hovering {
    RoundedRectangle(cornerRadius:8,style:.continuous)
     .fill(accent.tint.opacity(0.05))
     .overlay(RoundedRectangle(cornerRadius:8,style:.continuous).strokeBorder(accent.tint.opacity(0.7),lineWidth:2))
     .padding(3).transition(.opacity).allowsHitTesting(false)
   }
  }.overlay(alignment:.bottom) {
   if hovering {
    Label {
     Text(originals ? "Add to Originals" : "Add to Exports").foregroundStyle(.primary)
    } icon: {
     Image(systemName:"plus.circle.fill").foregroundStyle(accent.tint)
    }.font(.system(size:12,weight:.medium))
     .padding(.horizontal,14).padding(.vertical,8).background(.regularMaterial,in:Capsule())
     .padding(.bottom,16).transition(reduceMotion ? .identity : .opacity.combined(with:.offset(y:4)))
     .allowsHitTesting(false)
   }
  }
  .animation(reduceMotion ? nil : .easeOut(duration:0.16),value:hovering)
  .dropDestination(for:URL.self) { urls,_ in library.receive(urls,originals:originals) } isTargeted: { hovering=$0 && !library.busy }
  .onChange(of:library.busy) { if $0 { hovering=false } }
  .frame(minWidth:280,maxWidth:.infinity,maxHeight:.infinity).background(Vibrancy(material: .underWindowBackground, blendingMode: .behindWindow))
 }
}
func category(_ row:MetadataRow)->String {
 let key=row.name.lowercased(), group=row.group.lowercased()
 if key.contains("gps") || group.contains("gps") || key.contains("latitude") || key.contains("longitude") || key.contains("location") || ["city","country","countrycode","state","province-state","sublocation"].contains(key) {return "Location"}
 if key.contains("date") || key.contains("time") && !key.contains("duration") && !key.contains("scale") && key != "exposuretime" {return "Dates & Times"}
 if key.contains("lens") || ["make","model","serialnumber","cameramodelname","software"].contains(key) {return "Camera & Lens"}
 if group == "capture" || key == "fnumber" || key.contains("exposure") || key.contains("aperture") || key.contains("shutter") || key.contains("focal") || key.contains("iso") || key.contains("flash") || key.contains("whitebalance") {return "Capture Settings"}
 if key.contains("audio") || key.contains("sample") || key.contains("channel") || group.contains("audio") {return "Audio"}
 if key.contains("color") || key.contains("colour") || key.contains("profile") || key.contains("gamma") || key.contains("primaries") {return "Colour"}
 if key.contains("video") || key.contains("frame") || key.contains("compressor") || key.contains("bitrate") {return "Video"}
 if ["filetype","filesize","mimetype","imagesize","imagewidth","imageheight","duration","rotation","megapixels"].contains(key) {return "Information"}
 return "Other Metadata"
}
func readable(_ text:String)->String { text.replacingOccurrences(of:"([a-z])([A-Z])",with:"$1 $2",options:.regularExpression) }
struct Inspector: View {
 @ObservedObject var library:Library
 let categories=["Information","Camera & Lens","Capture Settings","Dates & Times","Location","Video","Audio","Colour","Other Metadata"]
 @Environment(\.accessibilityReduceMotion) private var reduceMotion
 @Environment(\.joongaAccent) private var accent
 @State private var metadataSearch = ""
 @State private var expanded:Set<String>=["Information","Camera & Lens","Capture Settings","Dates & Times","Location","Video","Audio","Colour"]
 var searchQuery: String { metadataSearch.trimmingCharacters(in: .whitespacesAndNewlines) }
 var multiSelection: Bool { library.selectedTargets.count > 1 }
 var canCompare: Bool { library.mode == .transfer && (multiSelection || library.selected?.status == "Matched") }
 var differencesOnly: Bool { canCompare && library.changesOnly && !library.editing }
 var rows:[MetadataRow] {
  (library.editing ? metadataEditingRows(library.metadata) : library.metadata).filter { row in
   (!differencesOnly || row.changed) && (searchQuery.isEmpty ||
    "\(category(row)) \(readable(row.name)) \(row.id) \(row.original) \(row.exported)".localizedCaseInsensitiveContains(searchQuery))
  }
 }
 var previewURL:URL? { if library.mode != .transfer { return library.selectedTargets.first }; return library.activeOriginals ? library.selected?.original : library.selected?.exported }
 var body: some View {
  VStack(spacing:0) {
   if let selected=library.selected {
    let visibleRows = rows
    let groupedRows = Dictionary(grouping: visibleRows, by: category)
    ScrollViewReader { scroll in
    ScrollView {
     LazyVStack(alignment:.leading,spacing:0) {
      VStack(alignment:.leading,spacing:14) {
       if !multiSelection {
        Thumbnail(url: previewURL, cornerRadius: 8, largePreview: true)
         .frame(maxWidth: .infinity)
         .frame(height: 210)
         .shadow(color: .black.opacity(0.28), radius: 8, y: 3)
         .padding(.top, 4)
         .padding(.bottom, 4)
       }
       VStack(alignment:.leading,spacing:4) {
        Text(multiSelection ? "\(library.selectedTargets.count) Files Selected" : (previewURL ?? selected.exported).lastPathComponent)
         .font(.system(size:16,weight:.semibold)).textSelection(.enabled)
        Text(multiSelection ? (library.mode == .transfer ? (library.activeOriginals ? "Selected originals" : "Selected exports") : "Selected files") : fileDescription(previewURL ?? selected.exported))
         .font(.system(size:12.5)).foregroundStyle(.secondary)
        if !multiSelection && selected.status == "Matched" {
         MetadataStatusLabel(title: library.activeOriginals ? "Matched to export" : "Matched to original",
                             symbol: "checkmark.circle.fill", color: .green)
          .font(.system(size:10)).padding(.top,3)
        }
       }
       Divider().opacity(0.45)
       if canCompare && !library.editing {
        Picker("Metadata",selection:$library.changesOnly) { Text("Differences").tag(true); Text("All Metadata").tag(false) }.pickerStyle(.segmented).labelsHidden()
       }
       HStack(spacing: 8) {
        HStack(spacing:6) {
         Image(systemName:"magnifyingglass").foregroundStyle(.secondary)
         TextField("Search metadata",text:$metadataSearch).textFieldStyle(.plain)
         if !metadataSearch.isEmpty { Button { metadataSearch="" } label:{Image(systemName:"xmark.circle.fill")}.buttonStyle(.plain).foregroundStyle(.secondary).help("Clear search") }
        }.font(.system(size:11)).padding(7).background(.thinMaterial,in:RoundedRectangle(cornerRadius:7,style:.continuous))
         .overlay(RoundedRectangle(cornerRadius:7,style:.continuous).strokeBorder(Color.primary.opacity(0.08)))
        if !groupedRows.isEmpty {
         Menu {
          ForEach(categories.filter { groupedRows[$0] != nil }, id: \.self) { section in
           Button(section) {
            expanded.insert(section)
            withAnimation(reduceMotion ? nil : .easeInOut(duration: 0.2)) { scroll.scrollTo(section, anchor: .top) }
           }
          }
         } label: {
          Image(systemName: "list.bullet")
           .font(.system(size: 12))
           .frame(width: 26, height: 26)
         }
         .menuStyle(.borderlessButton)
         .menuIndicator(.hidden)
         .fixedSize()
         .tint(.secondary)
         .help("Jump to Section")
        }
       }
       if library.mode == .transfer { Toggle("Edit metadata", isOn: $library.editing).toggleStyle(.switch).tint(accent.tint).controlSize(.small).disabled(library.busy || library.exif == nil) }
       if library.editing {
        let pendingLocations = Set(library.editingTargets.compactMap { library.locationDrafts[$0.path] })
        if !pendingLocations.isEmpty {
         MetadataStatusLabel(title: pendingLocations.count == 1 ? "Pending location · " + (pendingLocations.first?.text ?? "") : "Multiple pending locations",
                             symbol: "mappin.circle").font(.caption)
        }
        Text("Blank values remove a tag when saved.").font(.caption).foregroundStyle(.secondary)
        if library.mode == .transfer && library.selectedTargets.count < 2 { Picker("Edit file", selection: $library.editOriginal) {
         Text("Export").tag(false)
         Text("Original").tag(true)
        }.pickerStyle(.segmented).disabled(library.selected?.original == nil || library.busy) }
        Text(library.editingTargets.count > 1 ? "Changes apply to all \(library.editingTargets.count) selected files" : library.editingTargets.first?.path ?? "Select a file").font(.caption2).foregroundStyle(.secondary).textSelection(.enabled)
        HStack {
         Button("Revert") { for url in library.editingTargets { library.drafts[url.path]=nil; library.locationDrafts[url.path]=nil; library.workflowDrafts[url.path]=nil } }
          .disabled(!library.editingTargets.contains { library.pendingPaths.contains($0.path) } || library.busy)
         Spacer()
        }
        HStack {
         TextField("Tag, e.g. EXIF:Artist", text: $library.newTag)
         TextField("Value", text: $library.newValue)
         Button { library.stage(library.newTag, value: library.newValue); library.newTag=""; library.newValue="" } label: { Image(systemName:"plus.circle") }.disabled(library.newTag.isEmpty)
        }.textFieldStyle(.roundedBorder).disabled(library.busy || library.reading)
       }
      }.padding(.bottom, 12)
      if library.reading { ProgressView().frame(maxWidth:.infinity).padding() }
      else {
       if visibleRows.isEmpty { Label(!searchQuery.isEmpty ? "No matching metadata" : differencesOnly ? "No metadata differences" : "No metadata available",systemImage:searchQuery.isEmpty ? "checkmark.circle" : "magnifyingglass").font(.system(size:11)).foregroundStyle(.secondary).padding(.vertical,12) }
       ForEach(categories,id: \.self) { section in
        let values=groupedRows[section] ?? []
        let showMap = library.mode == .transfer && section == "Location" && !library.selectedTargets.isEmpty &&
         (searchQuery.isEmpty || "Location GPS Map".localizedCaseInsensitiveContains(searchQuery))
        if !values.isEmpty || showMap {
         let isOpen = expanded.contains(section)
         Button {
          withAnimation(reduceMotion ? nil : .easeInOut(duration:0.16)) {
           if isOpen { expanded.remove(section) } else { expanded.insert(section) }
          }
         } label: {
          HStack(spacing: 6) {
           Image(systemName: "chevron.right")
            .font(.system(size: 9, weight: .semibold))
            .foregroundStyle(.secondary)
            .rotationEffect(.degrees(isOpen ? 90 : 0))
            .frame(width: 10)
           Text(section).font(.system(size:12,weight:.semibold)).foregroundStyle(.primary)
           Spacer()
           if !values.isEmpty {
            Text("\(values.count)").font(.system(size: 10)).foregroundStyle(.tertiary)
           }
          }
          .padding(.top, 12)
          .padding(.bottom, 6)
          .contentShape(Rectangle())
         }
         .buttonStyle(.plain)
         .id(section)
         if isOpen {
          if showMap {
           if !library.locationPins.isEmpty { LocationMap(pins:library.locationPins).padding(.vertical,8) }
           Button {
            library.openMapForSelection()
           } label: {
            Label("Open Map Workspace…", systemImage: "mappin.and.ellipse")
           }
           .controlSize(.small)
           .disabled(library.busy || library.exif == nil)
           .frame(maxWidth: .infinity, alignment: .leading)
           .padding(.bottom, 8)
          }
          ForEach(values) { row in
           HStack(alignment:.top,spacing:12) {
            Text(readable(row.name)).font(.system(size:11)).foregroundStyle(.secondary).frame(maxWidth:.infinity,alignment:.leading)
            VStack(alignment:.trailing,spacing:4) {
             if library.editing, let tag=editableMetadataTag(row,rows:library.metadata) { MetadataValueEditor(library: library, row: row, tag:tag).disabled(library.busy) }
             else if row.mixed { MetadataStatusLabel(title: "Multiple values", symbol: "circle.lefthalf.filled") }
             else {
              MetadataValueDisplay(row:row, preferOriginal:library.editing ? library.editOriginal : library.activeOriginals, hasOriginal:selected.original != nil)
              if library.editing { Label(row.group == "Composite" ? "Calculated" : "Read-only",systemImage:"lock").font(.system(size:9)).foregroundStyle(.secondary).help(row.group == "Composite" ? "ExifTool calculates this value from other metadata; edit the underlying capture tags instead." : "This is file information, not an editable metadata field.") }
             }
            }.font(.system(size:11)).multilineTextAlignment(.trailing).textSelection(.enabled).frame(maxWidth:.infinity,alignment:.trailing)
           }.padding(.vertical,7)
           Divider().opacity(0.5)
          }
         }
        }
       }
      }
     }.padding(18)
    }
    }
   } else {
    VStack(spacing: 0) {
     Spacer()
     VStack(spacing:10){Image(systemName:"doc.text.magnifyingglass").font(.system(size:30,weight:.ultraLight)).foregroundStyle(.tertiary); Text("No Selection").font(.system(size:13,weight:.medium)); Text("Select a file to preview its metadata.").font(.system(size:11)).foregroundStyle(.secondary)}.padding(.vertical,20).padding(.horizontal,24).background(.thinMaterial,in:RoundedRectangle(cornerRadius:12,style:.continuous)).overlay(RoundedRectangle(cornerRadius:12,style:.continuous).strokeBorder(Color.primary.opacity(0.08))).padding(20)
     Spacer()
    }
   }
  }.frame(minWidth: library.mode == .transfer ? 330 : 340, maxWidth: library.mode == .transfer ? 330 : .infinity, maxHeight:.infinity).background(Vibrancy())

 }
 func fileDescription(_ url:URL)->String { let values=try? url.resourceValues(forKeys:[.fileSizeKey,.localizedTypeDescriptionKey]); let size=ByteCountFormatter.string(fromByteCount:Int64(values?.fileSize ?? 0),countStyle:.file); return "\(values?.localizedTypeDescription ?? url.pathExtension.uppercased()) · \(size)" }
}
struct Workspace:View {
 @State private var showPendingFiles = false
 @ObservedObject var library:Library
 @Environment(\.joongaAccent) private var accent
 @Environment(\.accessibilityReduceMotion) private var reduceMotion
 var body:some View {
  VStack(spacing:0) {
   if library.mode == .transfer {
    HSplitView { FilePane(library:library,originals:true); FilePane(library:library,originals:false); if library.showInspector { Inspector(library:library) } }.frame(maxWidth:.infinity,maxHeight:.infinity)
   } else { MediaWorkspace(library: library) }
   Divider()
   HStack(spacing:12) {
    if library.busy || library.mediaLoading { ProgressView().controlSize(.small) }
    Text(library.message.isEmpty ? (library.mode == .transfer ? "\(library.matched) matched" : "\(library.mediaItems.count) files") : library.message).font(.system(size:11)).foregroundStyle(.secondary)
    Spacer()
    Text("\(library.selectedTargets.count) selected").font(.system(size:10)).foregroundStyle(.secondary)
    if !library.pendingPaths.isEmpty {
     Button { showPendingFiles.toggle() } label: {
      MetadataStatusLabel(title: "\(library.pendingPaths.count) pending", symbol: "circle.fill")
     }.buttonStyle(.plain).popover(isPresented: $showPendingFiles) {
      VStack(alignment: .leading, spacing: 8) {
       Text("Files with unsaved changes").font(.headline)
       Text("Save Changes writes the staged metadata to these files.").font(.caption)
       ScrollView { VStack(alignment: .leading) { ForEach(library.pendingPaths.sorted(), id: \.self) { path in
        Text(URL(fileURLWithPath: path).lastPathComponent).help(path)
       } } }.frame(maxHeight: 240)
      }.padding()
     }
      .font(.caption)
     Button("Save Changes", action: library.saveEdits).buttonStyle(.borderedProminent).tint(accent.tint).disabled(library.busy || library.mediaLoading)
    } else if library.mode != .transfer {
     Button("Save Changes", action: library.saveEdits).disabled(true)
    }
    if library.mode == .transfer {
     Button("Copy Metadata",action:library.transfer).buttonStyle(.borderedProminent).tint(accent.tint).controlSize(.regular).disabled(library.busy || library.matched==0 || library.exif==nil)
    }
   }.padding(.horizontal,16).frame(height:48).background(Vibrancy(material: .headerView, blendingMode: .behindWindow))
  }
  .overlay(alignment: .top) {
   if let progress = library.loadingProgress {
    GeometryReader { geo in
     ZStack(alignment: .leading) {
      Rectangle().fill(Color.primary.opacity(0.08))
      Rectangle()
       .fill(accent.tint)
       .frame(width: geo.size.width * min(max(progress, 0), 1))
       .animation(reduceMotion ? nil : .easeOut(duration: 0.18), value: progress)
     }
    }
    .frame(height: 2.5)
    .allowsHitTesting(false)
    .transition(.opacity)
   }
  }
  .background(Vibrancy(material: .underWindowBackground, blendingMode: .behindWindow).ignoresSafeArea(.all, edges: .top))
   .sheet(isPresented: $library.showSettings) { SettingsView(library: library) }
   .sheet(isPresented: $library.showWorkflows) { WorkflowView(library:library) }.sheet(isPresented:Binding(get:{library.report != nil && !library.showWorkflows && !library.showSettings},set:{if !$0{library.report=nil}})) { OperationResults(library:library) }
 }
}
final class App: NSObject, NSApplicationDelegate, NSWindowDelegate, NSToolbarDelegate, NSToolbarItemValidation, NSMenuItemValidation, NSMenuDelegate {
 #if VISUAL_REVIEW
 let library=Library(workflowFile:FileManager.default.temporaryDirectory.appendingPathComponent("MetadataTransfer-Review-" + UUID().uuidString + "/Workflows.json"))
 #else
 let library=Library()
 #endif
 private let productName = Bundle.main.object(forInfoDictionaryKey: "CFBundleName") as? String ?? "Joonga Metadata"
 private weak var workflowMenu: NSMenu?
 private var workflowCommandCount = 0
 private var workspaceClosed = false
 private weak var workspaceControl: NSSegmentedControl?
 var window:NSWindow!
 func applicationShouldTerminateAfterLastWindowClosed(_ sender:NSApplication)->Bool { true }
 func windowShouldClose(_ sender: NSWindow) -> Bool {
  sender.attachedSheet == nil && confirmClose()
 }
 func windowWillClose(_ notification: Notification) { workspaceClosed = true }
 func applicationShouldTerminate(_ sender: NSApplication) -> NSApplication.TerminateReply {
  (workspaceClosed || (window?.attachedSheet == nil && confirmClose())) ? .terminateNow : .terminateCancel
 }
 private func confirmClose() -> Bool {
  let alert = NSAlert()
  if library.busy {
   alert.messageText = "An operation is still running"
   alert.informativeText = "Wait for it to finish before closing Joonga Metadata."
   alert.runModal()
   return false
  }
  guard !library.pendingPaths.isEmpty else { return true }
  alert.messageText = "Discard unsaved metadata edits?"
  alert.informativeText = "Pending edits will be lost if you close Joonga Metadata."
  alert.addButton(withTitle: "Keep Editing")
  alert.addButton(withTitle: "Discard Edits").hasDestructiveAction = true
  return alert.runModal() == .alertSecondButtonReturn
 }
 func applicationDidFinishLaunching(_ notification:Notification) {
  installMenus()
  window=FilePreviewWindow(contentRect:NSRect(x:0,y:0,width:1240,height:760),styleMask:[.titled,.closable,.miniaturizable,.resizable],backing:.buffered,defer:false)
  window.delegate=self
  if let previewWindow=window as? FilePreviewWindow {
   previewWindow.selectedFiles = { [weak self] in self?.library.selectedTargets ?? [] }
   previewWindow.selectedRow = { [weak self] in
    guard let library = self?.library else { return nil }
    if library.mode != .transfer { return library.selectedTargets.first.map { QuickLookRow(id: $0.path, originals: false) } }
    guard let id = library.selection else { return nil }
    return QuickLookRow(id: id, originals: library.activeOriginals)
   }
   previewWindow.selectedURL = { [weak self] in
    guard let library = self?.library, let pair = library.selected else { return nil }
    return library.mode == .transfer && library.activeOriginals ? pair.original : pair.exported
   }
   previewWindow.enableQuickLook()
  }
  window.title=productName; window.titleVisibility = .hidden; window.titlebarAppearsTransparent=false; window.toolbarStyle = .unified; window.minSize=NSSize(width:920,height:540); window.contentView=NSHostingView(rootView: Workspace(library: library).modifier(MacAccentModifier())); let toolbar=NSToolbar(identifier:"WorkspaceToolbar"); toolbar.delegate=self; toolbar.displayMode = .iconOnly; toolbar.centeredItemIdentifiers = [.init("workspace")]; window.toolbar=toolbar; window.toolbar?.showsBaselineSeparator = false; window.center(); window.setFrameAutosaveName("MetadataTransferWorkspace"); window.makeKeyAndOrderFront(nil);
  #if VISUAL_REVIEW
  if let sample=Bundle.main.resourceURL?.appendingPathComponent("Samples") {
   library.mode = .transfer; library.source=sample.appendingPathComponent("Originals"); library.destination=sample.appendingPathComponent("Exports"); library.scan()
  }
  #else
  if library.destination != nil { library.scan() }
  #endif
   NSApp.activate(ignoringOtherApps:true)
 }
 func toolbarAllowedItemIdentifiers(_ toolbar:NSToolbar)->[NSToolbarItem.Identifier] {
  [.init("originals"),.init("clear"),.init("exports"),.init("refresh"),
   .flexibleSpace,.init("workspace"),.flexibleSpace,
   .init("workflows"),.init("favourites"),.init("inspector")]
 }
 func toolbarDefaultItemIdentifiers(_ toolbar:NSToolbar)->[NSToolbarItem.Identifier] { toolbarAllowedItemIdentifiers(toolbar) }
 func toolbar(_ toolbar:NSToolbar,itemForItemIdentifier id:NSToolbarItem.Identifier,willBeInsertedIntoToolbar:Bool)->NSToolbarItem? {
  if id.rawValue == "favourites" {
   let item = NSToolbarItem(itemIdentifier: id)
   item.label = "Favourites"
   item.view = NSHostingView(rootView: FavouriteToolbar(library: library, save: { [weak self] in
    self?.saveFavouriteLocation()
   }).modifier(MacAccentModifier()))
   updateWorkspaceTool(item, mode: library.mode)
   return item
  }
  if id.rawValue == "workspace" {
   let item = NSToolbarItem(itemIdentifier: id)
   item.label = "Workspace"
   let control = NSSegmentedControl(labels: WorkspaceMode.allCases.map(\.rawValue), trackingMode: .selectOne,
                                    target: self, action: #selector(changeWorkspace(_:)))
   control.segmentStyle = .rounded
   control.setAccessibilityLabel("Workspace mode")
   control.selectedSegment = WorkspaceMode.allCases.firstIndex(of: library.mode) ?? 0
   item.view = control
   item.visibilityPriority = .user
   workspaceControl = control
   library.onModeChanged = { [weak self] mode in
    self?.workspaceControl?.selectedSegment = WorkspaceMode.allCases.firstIndex(of: mode) ?? 0
    self?.updateWorkspaceTools(mode)
   }
   return item
  }
  let values:[String:(String,String,Selector)] = ["workflows":("Workflows","slider.horizontal.3",#selector(workflows)),"originals":("Add Files or Open Originals","plus",#selector(openOriginals)),"clear":("Clear Files","trash",#selector(clearFiles)),"exports":("Open Exports Folder","folder.badge.gearshape",#selector(openExports)),"refresh":("Refresh","arrow.clockwise",#selector(refresh)),"inspector":("Toggle Inspector","sidebar.right",#selector(inspector))]
  guard let (label,icon,action)=values[id.rawValue] else{return nil}
  let item=NSToolbarItem(itemIdentifier:id)
  item.label=label; item.toolTip=label; item.image=NSImage(systemSymbolName:icon,accessibilityDescription:label)
  item.target=self; item.action=action
  updateWorkspaceTool(item, mode:library.mode)
  return item
 }
 private func updateWorkspaceTools(_ mode: WorkspaceMode) {
  for item in window?.toolbar?.items ?? [] { updateWorkspaceTool(item, mode:mode) }
 }
 private func updateWorkspaceTool(_ item: NSToolbarItem, mode: WorkspaceMode) {
  let transferring = mode == .transfer
  switch item.itemIdentifier.rawValue {
  case "originals":
   let label = transferring ? "Open or Choose Originals Folder" : "Add Files or Folders"
   item.label = label; item.toolTip = label
   item.image = NSImage(systemSymbolName: transferring ? "folder" : "plus", accessibilityDescription: label)
  case "exports":
   if #available(macOS 15, *) { item.isHidden = !transferring }
   else { item.isEnabled = transferring }
  case "inspector":
   if #available(macOS 15, *) { item.isHidden = mode == .map }
   else { item.isEnabled = mode != .map }
  case "favourites":
   if #available(macOS 15, *) { item.isHidden = mode != .map }
   else { item.isEnabled = mode == .map }
  default: break
  }
 }
 func installMenus() {
  let main = NSMenu()
  func addMenu(_ title: String) -> NSMenu {
   let item = NSMenuItem(title:title,action:nil,keyEquivalent:"")
   let menu = NSMenu(title:title)
   item.submenu=menu; main.addItem(item); return menu
  }
  @discardableResult
  func command(_ menu: NSMenu, _ title: String, _ action: Selector, key: String = "") -> NSMenuItem {
   let item = menu.addItem(withTitle: title, action: action, keyEquivalent: key)
   item.target = self
   return item
  }

  let appMenu = addMenu(productName)
  command(appMenu, "About \(productName)", #selector(showAbout))
  appMenu.addItem(.separator())
  command(appMenu, "Settings…", #selector(settings), key: ",")
  appMenu.addItem(.separator())
  appMenu.addItem(withTitle:"Quit \(productName)",action:#selector(NSApplication.terminate(_:)),keyEquivalent:"q")

  let file = addMenu("File")
  command(file, "Add Files or Folders…", #selector(addFilesOrFolders), key: "o").keyEquivalentModifierMask = [.command, .option]
  command(file, "Remove Selected Files", #selector(removeSelectedFiles))
  command(file, "Clear All Files", #selector(clearFiles))
  file.addItem(.separator())
  command(file, "Choose Originals Folder…", #selector(originals), key: "o")
  command(file, "Choose Exports Folder…", #selector(exports), key: "o").keyEquivalentModifierMask = [.command, .shift]
  command(file, "Open Originals in Finder", #selector(openOriginals))
  command(file, "Open Exports in Finder", #selector(openExports))
  file.addItem(.separator())
  command(file, "Copy Metadata to Matched Exports…", #selector(copyMetadata))
  command(file, "Save Changes", #selector(saveChanges), key: "s")
  file.addItem(.separator())
  file.addItem(withTitle:"Close Window",action:#selector(NSWindow.performClose(_:)),keyEquivalent:"w")

  let workflow = addMenu("Workflows")
  command(workflow, "Manage Workflows…", #selector(workflows))
  command(workflow, "New Workflow", #selector(newWorkflow), key: "n")
  command(workflow, "Open Workflow Library Folder", #selector(openWorkflowLibrary))
  workflowMenu = workflow
  workflowCommandCount = workflow.numberOfItems
  workflow.delegate = self
  let edit=addMenu("Edit")
  edit.addItem(withTitle:"Undo",action:Selector(("undo:")),keyEquivalent:"z")
  let redo=edit.addItem(withTitle:"Redo",action:Selector(("redo:")),keyEquivalent:"z"); redo.keyEquivalentModifierMask=[.command,.shift]
  edit.addItem(.separator())
  edit.addItem(withTitle:"Cut",action:#selector(NSText.cut(_:)),keyEquivalent:"x")
  edit.addItem(withTitle:"Copy",action:#selector(NSText.copy(_:)),keyEquivalent:"c")
  edit.addItem(withTitle:"Paste",action:#selector(NSText.paste(_:)),keyEquivalent:"v")
  edit.addItem(withTitle:"Select All",action:#selector(NSText.selectAll(_:)),keyEquivalent:"a")
  edit.addItem(.separator())
  command(edit, "Copy Location", #selector(copyLocation), key: "c").keyEquivalentModifierMask = [.command, .shift]
  command(edit, "Paste Location", #selector(pasteLocation), key: "v").keyEquivalentModifierMask = [.command, .shift]
  let view=addMenu("View")
  for (index, mode) in WorkspaceMode.allCases.enumerated() {
   command(view, "\(mode.rawValue) Workspace", #selector(selectWorkspaceFromMenu(_:)), key: String(index + 1)).tag = index
  }
  view.addItem(.separator())
  command(view, "Quick Look", #selector(quickLook))
  command(view, "Previous File", #selector(previous), key: "[").keyEquivalentModifierMask = [.command, .option]
  command(view, "Next File", #selector(next), key: "]").keyEquivalentModifierMask = [.command, .option]
  command(view, "Refresh", #selector(refresh), key: "r")
  let inspectorItem=view.addItem(withTitle:"Inspect Metadata",action:#selector(inspector),keyEquivalent:"i"); inspectorItem.target=self; inspectorItem.keyEquivalentModifierMask=[.command]
  let windowMenu=addMenu("Window"); NSApp.windowsMenu=windowMenu
  windowMenu.addItem(withTitle:"Minimize",action:#selector(NSWindow.performMiniaturize(_:)),keyEquivalent:"m")
  NSApp.mainMenu=main
 }
 @objc func newWorkflow(){ guard window?.attachedSheet == nil || library.showWorkflows else { return }; library.requestNewWorkflow=true; library.showWorkflows=true }
 @objc private func settings() {
  guard window?.attachedSheet == nil, !library.busy else { return }
  library.showSettings = true
 }
 @objc private func changeWorkspace(_ control: NSSegmentedControl) {
  let index = control.selectedSegment
  guard !library.busy, WorkspaceMode.allCases.indices.contains(index) else {
   control.selectedSegment = WorkspaceMode.allCases.firstIndex(of: library.mode) ?? 0
   return
  }
  library.mode = WorkspaceMode.allCases[index]
 }
 @objc private func selectWorkspaceFromMenu(_ sender: NSMenuItem) {
  guard !library.busy, WorkspaceMode.allCases.indices.contains(sender.tag) else { return }
  library.mode = WorkspaceMode.allCases[sender.tag]
 }
 @objc private func openOriginals() {
  if library.mode == .transfer { library.openOrChooseFolder(originals: true) }
  else if !library.busy { library.chooseMedia() }
 }
 @objc private func openExports() { library.openOrChooseFolder(originals: false) }
 @objc private func addFilesOrFolders() {
  if library.mode == .transfer { library.choose(true) }
  else if !library.busy { library.chooseMedia() }
 }
 @objc private func removeSelectedFiles() { library.removeMedia(library.mediaSelection) }
 @objc private func clearFiles() { library.clearMedia() }
 private var hasLoadedFiles: Bool {
  library.mode == .transfer ? (!library.files.isEmpty || !library.unmatchedOriginals.isEmpty || library.source != nil || library.destination != nil) : !library.mediaItems.isEmpty
 }
 func validateToolbarItem(_ item: NSToolbarItem) -> Bool {
  switch item.itemIdentifier.rawValue {
  case "clear": return !library.busy && !library.mediaLoading && hasLoadedFiles
  default: return true
  }
 }
 @objc private func openWorkflowLibrary() { library.openWorkflowDirectory() }
 @objc private func showAbout() {
  guard let window, window.attachedSheet == nil else { return }
  let sheet = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 483, height: 220),
                       styleMask: [.titled], backing: .buffered, defer: false)
  sheet.title = "About \(productName)"
  sheet.contentViewController = NSHostingController(rootView: AboutJoongaView(
   close: { [weak window, weak sheet] in
    if let sheet { window?.endSheet(sheet) }
   },
   canInstall: { [weak self] in
    guard let library = self?.library else { return false }
    return !library.busy && !library.mediaLoading && library.pendingPaths.isEmpty
      && NSRunningApplication.runningApplications(withBundleIdentifier: Bundle.main.bundleIdentifier ?? "").count <= 1
   },
   restart: { [weak window, weak sheet] in
    if let sheet { window?.endSheet(sheet) }
    NSApp.terminate(nil)
   }).modifier(MacAccentModifier()))
  window.beginSheet(sheet)
 }
 @objc private func quickLook() { library.quickLook(library.selectedTargets) }
 @objc private func copyMetadata() { library.transfer() }
 @objc private func applySavedWorkflow(_ item: NSMenuItem) {
  guard canApplySavedWorkflow,
        let id = item.representedObject as? String,
        let preset = library.presets.first(where: { $0.id == id }) else { return }
  library.writeMetadata(preset.operations, to: library.selectedTargets)
 }

 private var canApplySavedWorkflow: Bool {
  window != nil && window.attachedSheet == nil && !library.busy && library.exif != nil && !library.selectedTargets.isEmpty
 }

 func menuNeedsUpdate(_ menu: NSMenu) {
  guard menu === workflowMenu else { return }
  while menu.numberOfItems > workflowCommandCount {
   menu.removeItem(at: menu.numberOfItems - 1)
  }
  menu.addItem(.separator())
  if library.presets.isEmpty {
   let empty = NSMenuItem(title: "No Saved Workflows", action: nil, keyEquivalent: "")
   empty.isEnabled = false
   menu.addItem(empty)
   return
  }
  if library.selectedTargets.isEmpty {
   let hint = NSMenuItem(title: "Select Files to Apply a Workflow", action: nil, keyEquivalent: "")
   hint.isEnabled = false
   menu.addItem(hint)
  }
  let presets = library.presets.sorted { $0.name.localizedStandardCompare($1.name) == .orderedAscending }
  for preset in presets {
   let item = menu.addItem(withTitle: preset.name, action: #selector(applySavedWorkflow(_:)), keyEquivalent: "")
   item.target = self
   item.representedObject = preset.id
  }
 }
 @objc private func saveChanges() {
  if library.showWorkflows { library.saveWorkflowRequest = UUID() }
  else { library.saveEdits() }
 }
 @objc private func copyLocation() { library.copySelectedLocation() }
 @objc private func pasteLocation() {
  if let point = library.pastedLocation() { library.stageLocation(point, targets: library.selectedTargets) }
 }
 @objc private func saveFavouriteLocation() {
  guard library.selectedMapLocation != nil || library.lastMapPoint != nil else {
   library.message = "Pick a map point or select a geotagged file to save a favourite."
   return
  }
  library.requestSaveFavourite = true
 }

 func validateMenuItem(_ item: NSMenuItem) -> Bool {
  let workspaceAvailable = window != nil && window.attachedSheet == nil && !library.busy
  switch item.action {
  case #selector(selectWorkspaceFromMenu(_:)):
   item.state = item.tag == WorkspaceMode.allCases.firstIndex(of:library.mode) ? .on : .off
   return workspaceAvailable
  case #selector(openOriginals): return workspaceAvailable && library.mode == .transfer && library.source != nil
  case #selector(openExports): return workspaceAvailable && library.mode == .transfer && library.destination != nil
  case #selector(removeSelectedFiles): return workspaceAvailable && library.mode != .transfer && !library.mediaSelection.isEmpty
  case #selector(clearFiles): return workspaceAvailable && !library.mediaLoading && hasLoadedFiles
  case #selector(copyMetadata): return workspaceAvailable && library.mode == .transfer && library.matched > 0 && library.exif != nil
  case #selector(copyLocation): return workspaceAvailable && library.selectedLocation != nil
  case #selector(pasteLocation): return workspaceAvailable && library.exif != nil && !library.selectedTargets.isEmpty
  case #selector(applySavedWorkflow(_:)): return canApplySavedWorkflow
  case #selector(saveChanges):
   item.title = library.showWorkflows ? "Save Workflow" : "Save Metadata Changes"
   return library.showWorkflows || (workspaceAvailable && !library.pendingPaths.isEmpty && library.exif != nil)
  case #selector(quickLook): return workspaceAvailable && !library.selectedTargets.isEmpty
  case #selector(previous): return workspaceAvailable && library.navigationTarget(-1) != nil
  case #selector(next): return workspaceAvailable && library.navigationTarget(1) != nil
  case #selector(inspector):
   if library.mode == .transfer {
    item.title = library.showInspector ? "Hide Preview" : "Show Preview"
   } else if library.mode == .metadata {
    item.title = library.showInspector ? "Hide Inspector" : "Show Inspector"
   } else {
    item.title = "Show Inspector"
   }
   return workspaceAvailable
  case #selector(newWorkflow): return library.showWorkflows || workspaceAvailable
  default: return workspaceAvailable
  }
 }
 @objc func workflows(){ library.showWorkflows=true }
 @objc func originals(){if !library.busy{if library.mode == .transfer { library.choose(true) } else { library.chooseMedia() }}}
 @objc func exports(){if !library.busy{library.choose(false)}}
 @objc func previous(){library.navigate(-1)}
 @objc func next(){library.navigate(1)}
 @objc func refresh(){if !library.busy && !library.mediaLoading { if library.mode == .transfer { library.scan() } else { library.refreshMedia(library.mediaItems.map(\.id)) } }}
 @objc func inspector(){
  if library.mode == .map {
   library.showInspector = true
   library.mode = .metadata
  } else {
   withAnimation(NSWorkspace.shared.accessibilityDisplayShouldReduceMotion ? nil : .easeInOut(duration:0.18)){library.showInspector.toggle()}
  }
 }
}
let application=NSApplication.shared; let delegate=App(); application.delegate=delegate; application.setActivationPolicy(.regular); application.run()
