using ScreenSound.Services;
using ScreenSound.Models;
using System.Text;
using System.Text.Json;

string fixture = Path.Combine(AppContext.BaseDirectory, "fixtures-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(fixture);
var idA = "{0.0.0.00000000}.{11111111-1111-1111-1111-111111111111}";
var idB = "{0.0.0.00000000}.{22222222-2222-2222-2222-222222222222}";
var first = Path.Combine(fixture, "main.ini");
var second = Path.Combine(fixture, "secondary.ini");
string content = "[Rainmeter]\r\nUpdate=33\r\n;encoding: é\r\n[MeasureAudioOutput]\r\nMeasure=Plugin\r\nPlugin=AudioLevel\r\nID=" + idA + "\r\nDisabled=0\r\nFFTSize=4096\r\n[Unrelated]\r\nID=untouched\r\n";
File.WriteAllBytes(first, Encoding.Latin1.GetBytes(content));
File.WriteAllText(second, content.Replace(idA, idB), Encoding.Unicode);
var options = new RainmeterSyncService.Options { Enabled = true, Bindings = new()
{
    new() { MonitorDeviceName="MAIN", Config=@"Example\Main", SkinFile=first },
    new() { MonitorDeviceName="SECONDARY", Config=@"Example\Secondary", SkinFile=second }
}};
var refreshes = new List<string>();
var svc = new RainmeterSyncService(options, refreshes.Add);
List<MonitorAudioMapping> Maps(string? a, string? b) =>
    new[] { ("MAIN", a), ("SECONDARY", b) }.Where(x=>x.Item2!=null)
    .Select(x=>new MonitorAudioMapping{MonitorDeviceName=x.Item1,AudioDeviceId=x.Item2!}).ToList();
int passed=0, failed=0;
void Check(string name, bool ok) { Console.WriteLine($"{(ok?"PASS":"FAIL")} {name}"); if(ok)passed++;else failed++; }
Check("initial identical mapping does not refresh", svc.Sync(Maps(idA,idB)).Count==0 && refreshes.Count==0);
Check("changing main speaker updates only main skin", svc.Sync(Maps(idB,idB)).Count==0 && refreshes.SequenceEqual(new[]{options.Bindings[0].Config}) && Encoding.Latin1.GetString(File.ReadAllBytes(first))==content.Replace(idA,idB));
Check("main/secondary swap updates secondary independently", svc.Sync(Maps(idB,idA)).Count==0 && refreshes.Count==2 && File.ReadAllText(second)==content);
Check("UTF16 BOM preserved", File.ReadAllBytes(second).Take(2).SequenceEqual(new byte[]{255,254}));
var stamp = File.GetLastWriteTimeUtc(first);
for (int i=0;i<500;i++) svc.Sync(Maps(idB,idA));
Check("500 unchanged calls do not write or refresh", stamp==File.GetLastWriteTimeUtc(first) && refreshes.Count==2);
svc.Sync(Maps(null,idA));
Check("unassigned/unplugged main disables capture without following default", Encoding.Latin1.GetString(File.ReadAllBytes(first)).Contains("Disabled=1") && File.ReadAllBytes(first).Length>0 && refreshes.Count==3);
svc.Sync(Maps(idA,idA));
Check("reassign or reconnect re-enables capture", Encoding.Latin1.GetString(File.ReadAllBytes(first))==content && refreshes.Count==4);
var beforeBad = File.ReadAllBytes(first);
Check("invalid endpoint does not damage skin", svc.Sync(Maps("bad\nID=injected",idA)).Count==1 && beforeBad.SequenceEqual(File.ReadAllBytes(first)));
File.WriteAllText(first,"[Other]\nID=untouched\n");
Check("broken primary does not block secondary", svc.Sync(Maps(idB,idB)).Count==1 && File.ReadAllText(first)=="[Other]\nID=untouched\n" && File.ReadAllText(second).Contains(idB));
File.WriteAllBytes(first,Encoding.Latin1.GetBytes(content.Replace("Disabled=0\r\n", "")));
Check("failed skin is retried and missing option inserted", svc.Sync(Maps(idB,idB)).Count==0 && Encoding.Latin1.GetString(File.ReadAllBytes(first)).Contains("Disabled=0") && Encoding.Latin1.GetString(File.ReadAllBytes(first)).Contains(idB));
var configFile=Path.Combine(fixture,"bindings.json");
File.WriteAllText(configFile,JsonSerializer.Serialize(new RainmeterSyncService.Options()));
Check("loading empty sidecar makes no changes", RainmeterSyncService.Load(configFile).Sync(Maps(idA,idB)).Count==0);
File.WriteAllText(first,content);
int attempts=0;
var retry = new RainmeterSyncService(new RainmeterSyncService.Options{Enabled=true,Bindings=new(){options.Bindings[0]}}, _=>{if(++attempts==1)throw new IOException("simulated command failure");});
Check("failed refresh is retried even after file was saved", retry.Sync(Maps(idB,idB)).Count==1 && retry.Sync(Maps(idB,idB)).Count==0 && attempts==2);
Check("original backup is preserved across device changes", Encoding.Latin1.GetString(File.ReadAllBytes(first+".screensound.bak"))==content);
var disabled = new RainmeterSyncService(new RainmeterSyncService.Options{Bindings=options.Bindings}, _=>throw new Exception("unexpected refresh"));
var disabledBefore=File.ReadAllBytes(first);
Check("integration is opt-in and disabled sync does nothing", disabled.Sync(Maps(idA,idB)).Count==0 && disabledBefore.SequenceEqual(File.ReadAllBytes(first)));
Check("parent discovery ignores child measures", RainmeterSyncService.GetParentMeasures(content+"[Child]\nPlugin=AudioLevel\nParent=MeasureAudioOutput\n").SequenceEqual(new[]{"MeasureAudioOutput"}));
var limited = Path.Combine(fixture,"limited.ini");
File.WriteAllText(limited,content.Replace("Update=33","Update=0"));
var limitedOptions = new RainmeterSyncService.Options{Enabled=true,Bindings=new(){new(){MonitorDeviceName="MAIN",Config="Example",SkinFile=limited}}};
var limitedSync = new RainmeterSyncService(limitedOptions,_=>{});
Check("fast visualizers capped at 33ms", limitedSync.Sync(Maps(idA,idB)).Count==0 && File.ReadAllText(limited).Contains("Update=33"));
Check("restore recovers exact original skin", limitedSync.RestoreBackups().Count==0 && File.ReadAllText(limited)==content.Replace("Update=33","Update=0"));
File.WriteAllText(limited,content.Replace("Update=33","Update=100"));
Check("already slower visualizers are preserved", new RainmeterSyncService(limitedOptions,_=>{}).Sync(Maps(idA,idB)).Count==0 && File.ReadAllText(limited).Contains("Update=100"));
File.WriteAllText(configFile,"{\"Bindings\":null}");
var invalidOptions=RainmeterSyncService.Load(configFile);
Check("invalid configuration reports an error and stays disabled", invalidOptions.LoadError!=null && !invalidOptions.Configuration.Enabled);
File.WriteAllText(configFile,"{\"Enabled\":true,\"Bindings\":[null]}");
Check("null binding cannot crash startup", RainmeterSyncService.Load(configFile).LoadError!=null);
File.WriteAllText(configFile,"{\"Enabled\":true,\"Bindings\":[{\"MonitorDeviceName\":null}]}");
Check("missing binding fields cannot crash startup", RainmeterSyncService.Load(configFile).LoadError!=null);
var malformed=Path.Combine(fixture,"malformed.ini");
File.WriteAllText(malformed,content.Replace("ID="+idA,"ID="+idA+"\r\nID="+idB));
var malformedOptions=new RainmeterSyncService.Options{Enabled=true,Bindings=new(){new(){MonitorDeviceName="MAIN",Config="Malformed",SkinFile=malformed}}};
var malformedBefore=File.ReadAllBytes(malformed);
Check("ambiguous duplicate keys fail without writing", new RainmeterSyncService(malformedOptions,_=>{}).Sync(Maps(idB,idA)).Count==1 && malformedBefore.SequenceEqual(File.ReadAllBytes(malformed)));
Console.WriteLine($"RESULT {passed} passed, {failed} failed");
return failed==0?0:1;
