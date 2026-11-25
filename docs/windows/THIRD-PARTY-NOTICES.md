# Third-party components

- **Microsoft .NET / WPF 10**: self-contained runtime files are distributed with the app. Their licenses and notices are retained in `ThirdPartyLicenses`. Source: https://github.com/dotnet/wpf and https://github.com/dotnet/runtime.
- **Microsoft WebView2 SDK**: used only by the optional map window. See https://www.nuget.org/packages/Microsoft.Web.WebView2 and the SDK license shipped alongside the binaries. The installed Microsoft Edge WebView2 Runtime is used; it is not redistributed here.
- **ExifTool 13.64, Windows x64 distribution** by Phil Harvey and its Windows packaging contributors: included unmodified except renaming `exiftool(-k).exe` to `exiftool.exe` to disable pause-on-exit. The complete `exiftool_files` directory, source modules, license, Strawberry Perl license archive, and README are retained. Official source/download: https://exiftool.org/ and https://github.com/exiftool/exiftool. ExifTool is available under the same terms as Perl. See the bundled files for the exact licenses covering Perl and additional runtime components.
- **Leaflet 1.9.4**: renders the interactive map. The BSD 2-Clause license is included in `Assets/Map/LEAFLET-LICENSE.txt`. [Leaflet source](https://github.com/Leaflet/Leaflet/tree/v1.9.4)
- **OpenStreetMap**: the picker requests map tiles for the area in view. The pin coordinates are passed locally to Joonga Metadata for writing. [OpenStreetMap attribution and license](https://www.openstreetmap.org/copyright)
- **Esri** supplies online satellite imagery and hybrid labels. The map displays attribution when either style is active. [World Imagery and reference layers](https://developers.arcgis.com/web-scene-specification/objects/baseMap/)
- **Photon**: the Windows picker sends the submitted place name or address to Komoot’s public OpenStreetMap geocoder. [Photon project](https://github.com/komoot/photon)

No personal workflow JSON, source media, or private settings are bundled. Synthetic test media contains only a solid colour.
