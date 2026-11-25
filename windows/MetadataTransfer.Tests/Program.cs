using MetadataTransfer.Core;
using System.Text.Json;

var passed=0;
void Check(bool value,string label){if(!value)throw new Exception("FAILED: "+label);Console.WriteLine("PASS "+label);passed++;}
var root=Path.Combine(Path.GetTempPath(),"MetadataTransfer-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
try
{
    var source=Path.Combine(root,"originals");var export=Path.Combine(root,"exports");Directory.CreateDirectory(source);Directory.CreateDirectory(export);
    var a=Path.Combine(source,"Photo.ARW");var b=Path.Combine(export,"photo-HDR.AVIF");File.WriteAllText(a,"");File.WriteAllText(b,"");
    var pairs=Matching.Compare([a],[b]);Check(pairs.Length==1&&pairs[0].Matched,"case-insensitive HDR matching across image formats");
    Check(!Matching.Compare([a],[Path.Combine(export,"Photo.mp4")])[0].Matched,"never match photo to video");
    Check(!Matching.Compare([a,a.Replace(".ARW",".DNG")],[b])[0].Matched,"ambiguous originals skipped");
    Check(!Matching.Compare([a],[a])[0].Matched,"same path copy skipped");
    Directory.CreateDirectory(Path.Combine(source,"nested"));File.WriteAllText(Path.Combine(source,"nested","Clip.MP4"),"");
    Check(Matching.Expand([source]).Count==2,"recursive media expansion");
    Check(Matching.Expand([source,a]).Count==2,"overlapping drops deduplicated");
    var workflow=new Workflow("one","Camera","My Workflows",["-Make=Sony","-Model=ILCE-7C"]);
    var library=new WorkflowStore(Path.Combine(root,"Workflows.json"));library.Save([workflow]);Check(library.Load().Single()==workflow || library.Load().Single().Operations.SequenceEqual(workflow.Operations),"workflow disk round trip");
    var mac="""{"format":"MetadataTransferWorkflows","version":1,"presets":[{"id":"mac","name":"Mac camera","category":"My Workflows","operations":["-Make=Sony","-Model=ILCE-7C"]}]}""";
    Check(WorkflowStore.Decode(mac)[0].Operations.SequenceEqual(workflow.Operations),"Mac workflow archive compatibility");
    Check(WorkflowStore.Decode(JsonSerializer.Serialize(new[]{workflow},WorkflowStore.Json)).Length==1,"legacy workflow array import");
    Check(WorkflowStore.Merge([workflow],[workflow]).Length==1,"exact duplicate workflow skipped");
    Check(WorkflowStore.Merge([workflow],[workflow with {Operations=workflow.Operations.Reverse().ToArray()}]).Length==2,"operation order preserved on import");
    library.Save([]);Check(library.Load().Length==0,"deleted workflows remain deleted on restart");
    Check(!WorkflowRules.Valid("-execute")&&!WorkflowRules.Valid("-Artist=one\n-execute")&&WorkflowRules.Valid("-Artist=A B")&&WorkflowRules.Valid("-CreateDate<DateTimeOriginal"),"operation validation");
    var t1=new Dictionary<string,string>{{"IFD0:Make","Sony"},{"XMP-dc:Title","One"}};
    var t2=new Dictionary<string,string>{{"XMP-tiff:Make","Sony"},{"XMP-dc:Title","Two"}};
    var refs=new Dictionary<string,string>{{"ExifIFD:LensModel","Original lens"},{"Composite:Aperture","2.8"}};
    var fields=Metadata.Build([t1,t2],[refs]);
    Check(fields.Count(f=>f.Name=="Make")==1&&!fields.Single(f=>f.Name=="Make").Mixed,"camera aliases merge across selected files");
    Check(fields.Single(f=>f.Name=="Title").Mixed,"mixed values detected");
    Check(fields.Single(f=>f.Tag=="ExifIFD:LensModel").Value==""&&fields.Single(f=>f.Tag=="ExifIFD:LensModel").Writable,"reference-only missing fields remain editable without copying values");
    Check(fields.Any(f=>f.Name=="ExposureTime"&&f.Writable)&&fields.Any(f=>f.Name=="GPSLongitude"&&f.Writable),"missing common capture and location fields available");
    Check(fields.Single(f=>f.Tag=="Composite:Aperture").WriteTag=="FNumber","calculated aperture routes to stored tag");
    var missing=Metadata.Build([t1,new Dictionary<string,string>()],[]);Check(missing.Single(f=>f.Name=="Make").Mixed,"missing vs populated field is mixed");
    var executable=args.FirstOrDefault()??ExifService.Find(null);
    if(executable is not null)
    {
        var exif=new ExifService(executable);
        foreach(var extension in new[]{"jpg","mp4"})
        {
            var path=Path.Combine(root,"edit."+extension);File.Copy(Path.Combine(AppContext.BaseDirectory,"Fixtures","sample."+extension),path);
            var before=File.GetLastWriteTimeUtc(path);
            var result=await exif.WriteAsync(path,["-Make=Sony","-Model=ILCE-7C","-LensModel=Test Lens","-FNumber=2.8","-ExposureTime=1/125","-Artist=Literal $() ; text"]);
            Check(result.Success,extension+" write and camera read-back verification");
            var read=await exif.ReadAsync(path);Check(read.Any(x=>x.Key.EndsWith(":LensModel")&&x.Value=="Test Lens"),extension+" creates absent lens metadata");
            Check(read.Any(x=>x.Key.EndsWith(":Artist")&&x.Value=="Literal $() ; text"),extension+" arguments remain literal, no shell execution");
            Check(File.Exists(path+"_original"),extension+" backup retained");Check(Math.Abs((File.GetLastWriteTimeUtc(path)-before).TotalSeconds)<2,extension+" modification date preserved");
            var dest=Path.Combine(root,"copy."+extension);File.Copy(Path.Combine(AppContext.BaseDirectory,"Fixtures","sample."+extension),dest);
            var copied=await exif.CopyAsync(new(path,dest,"Matched"));Check(copied.Success,extension+" full writable metadata copy");
            Check((await exif.ReadAsync(dest)).Any(x=>x.Key.EndsWith(":Model")&&x.Value=="ILCE-7C"),extension+" copied camera model persisted");
        }
        foreach (var extension in new[] { "jpg", "mp4" })
        {
            var path = Path.Combine(root, "géotag sample." + extension);
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample." + extension), path);
            const double latitude = -33.4578123, longitude = -70.6482764;
            var result = await exif.WriteLocationAsync(path, latitude, longitude, ["-Artist=Workspace draft"]);
            var point = await exif.LocationAsync(path);
            Check(result.Success && point is not null && Math.Abs(point.Latitude - latitude) < 0.000001 &&
                Math.Abs(point.Longitude - longitude) < 0.000001, extension + " saves and verifies a south/west map pin");
            var tags = await exif.ReadAsync(path);
            Check(tags.Any(tag=>tag.Key.EndsWith(":Artist")&&tag.Value=="Workspace draft"),extension+" saves GPS and a metadata edit together");
            Check(extension == "jpg" ? tags.ContainsKey("GPS:GPSLatitudeRef") && tags.ContainsKey("GPS:GPSLongitudeRef")
                : tags.ContainsKey("Keys:GPSCoordinates"), extension + " writes native location tags");
            result = await exif.WriteLocationAsync(path, 0, 0);
            point = await exif.LocationAsync(path);
            Check(result.Success && point is { Latitude: 0, Longitude: 0 } && File.Exists(path + "_original"),
                extension + " replaces the pin with zero coordinates and keeps the backup");
        }
        var jpeg = Path.Combine(root,"géotag sample.jpg");
        var video = Path.Combine(root,"géotag sample.mp4");
        var noGps = Path.Combine(root,"edit.jpg");
        var batch = await exif.ReadWorkspaceBatchAsync([jpeg,video,noGps,Path.Combine(root,"missing.jpg")],CancellationToken.None);
        Check(batch.Count==3&&batch[jpeg].Location is {Latitude:0,Longitude:0}&&
            batch[video].Location is {Latitude:0,Longitude:0}&&batch[noGps].Location is null&&
            batch[jpeg].Tags.Values.Contains("Workspace draft"),"batch returns usable JPEG, MP4 and untagged rows despite a missing file");
    }
    else Console.WriteLine("SKIP ExifTool integration: pass the executable path to enable it.");
    Console.WriteLine($"{passed} checks passed.");
}
finally{Directory.Delete(root,true);}
