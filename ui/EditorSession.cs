#nullable enable
using AspectSwitch;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
namespace AspectSettings;
public sealed class UiRequest
{
    public Settings Settings { get; set; } = new();
    public bool StartAtLogin { get; set; }
    public string ControllerPath { get; set; } = "";
    public string SessionId { get; set; } = "";
}
public sealed class UiResponse
{
    public bool ResetAllData { get; set; }
    public Settings Settings { get; set; } = new();
    public bool StartAtLogin { get; set; }
    public string SessionId { get; set; } = "";
}
internal sealed class EditorSession
{
    internal static readonly JsonSerializerOptions Json = new() { IncludeFields = true, WriteIndented = true };
    internal Settings Draft { get; private set; }
    internal bool StartAtLogin { get; set; }
    internal bool Preview { get; }
    internal string InitialPage { get; }
    internal string? RenderPath { get; }
    internal string? SmokePath { get; }
    private readonly UiRequest request;
    private readonly string? responsePath;
    internal EditorSession(string[] args)
    {
        string? Value(string flag) { int p = Array.IndexOf(args, flag); return p >= 0 && p + 1 < args.Length ? args[p + 1] : null; }
        var requestPath = Value("--request"); responsePath = Value("--response");
        Preview = args.Contains("--preview") || requestPath == null;
        InitialPage = Value("--page") ?? "devices";
        if (!new[] { "devices", "compatibility", "general" }.Contains(InitialPage)) InitialPage = "devices"; RenderPath = Value("--render"); SmokePath = Value("--smoke");
        request = Preview ? new UiRequest { Settings = Settings.KnownMonitor() } : JsonSerializer.Deserialize<UiRequest>(File.ReadAllText(requestPath!, Encoding.UTF8), Json) ?? throw new Exception("설정을 읽지 못했습니다.");
        if (!Preview && (String.IsNullOrWhiteSpace(responsePath) || String.IsNullOrWhiteSpace(request.SessionId))) throw new Exception("설정 세션이 올바르지 않습니다.");
        request.Settings.Validate(); Draft = request.Settings.Copy(); StartAtLogin = request.StartAtLogin;
    }
    internal void AddDevice()
    {
        if (Draft.MonitorName.Length == 0 || Draft.AspectModes.Length == 0) throw new Exception("모니터 호환성에서 모니터와 화면 비율을 먼저 등록하세요.");
        var free = Draft.AvailableInputs.FirstOrDefault(c => !Draft.HasInput(c.Value));
        if (free == null) throw new Exception("등록 가능한 입력을 모두 사용하고 있습니다.");
        Draft.Devices.Add(new DeviceRule { Name = "새 기기 " + (Draft.Devices.Count + 1), Input = free.Value, Aspect = Draft.AspectModes[0].Value });
    }
    internal void Configure(MonitorCandidate candidate) => Draft = Capabilities.Configure(candidate, Draft);
    internal void Learn(MonitorCandidate candidate, string label) => Capabilities.Learn(Draft, candidate, label);
    internal void RemoveMonitor()
    {
        Draft = new Settings { PollMilliseconds = Draft.PollMilliseconds, AutoEnabled = Draft.AutoEnabled };
    }
    internal static MonitorCandidate[] SearchResults(Reading reading)
    {
        if (!reading.Ok) throw new Exception(String.IsNullOrWhiteSpace(reading.Error) ? "모니터 목록을 읽지 못했습니다. 연결과 DDC/CI 설정을 확인하세요." : reading.Error);
        return reading.Monitors ?? throw new Exception("모니터 검색 응답에 장치 목록이 없습니다.");
    }
    internal void ResetAllData()
    {
        if (!Preview) WriteResponse(new UiResponse { ResetAllData = true, Settings = new Settings(), StartAtLogin = false, SessionId = request.SessionId });
        Draft = new Settings(); StartAtLogin = false;
    }
    internal void Save()
    {
        Draft.Validate();
        if (Preview) return;
        var response = new UiResponse { Settings = Draft.Copy(), StartAtLogin = StartAtLogin, SessionId = request.SessionId };
        WriteResponse(response);
    }
    private void WriteResponse(UiResponse response)
    {
        string temp = responsePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(response, Json), new UTF8Encoding(false));
        File.Move(temp, responsePath!, true);
    }
    internal async Task<Reading> ReadHardware(string operation, MonitorCandidate? candidate = null)
    {
        // The UI has no path to input/aspect writes. Preview never launches a hardware worker.
        if (Preview) throw new Exception("미리보기에서는 모니터와 통신하지 않습니다.");
        if (operation != "list" && operation != "inspect") throw new InvalidOperationException("읽기 전용 작업만 지원합니다.");
        string token = candidate == null ? "-" : Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(candidate, Json)));
        var info = new ProcessStartInfo(request.ControllerPath) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        foreach (string arg in new[] { "--worker", operation, "-1", "-1", token }) info.ArgumentList.Add(arg);
        using var worker = Process.Start(info) ?? throw new Exception("모니터 확인 작업을 시작하지 못했습니다.");
        Task<string> output = worker.StandardOutput.ReadToEndAsync(); Task<string> errors = worker.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(operation == "inspect" ? 24000 : 10000);
        try { await worker.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { try { worker.Kill(); } catch { } throw new Exception("모니터 응답 시간이 초과됐습니다."); }
        if (worker.ExitCode != 0)
        {
            string detail = (await errors).Trim();
            throw new Exception("모니터 확인 실패 (" + Path.GetFileName(request.ControllerPath) + ", 종료 코드 " + worker.ExitCode + "). " +
                (detail.Length > 0 ? detail : "실제 AspectSwitch.exe에서 설정을 다시 열어 주세요. 테스트 창에서는 검색을 지원하지 않습니다."));
        }
        return JsonSerializer.Deserialize<Reading>(await output, Json) ?? throw new Exception("모니터 응답이 비어 있습니다.");
    }
}
