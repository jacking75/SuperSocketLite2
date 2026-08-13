using System.Text.Json;
using System.Text.Json.Serialization;

namespace SuperSocketLite.LoadTest.Report;

/// <summary>
/// 합격·불합격을 가르는 기준입니다. 코드가 아니라 파일로 두어 실행마다 바꿀 수 있게 합니다.
/// </summary>
public sealed class Thresholds
{
    /// <summary>기준 실행 대비 p99 지연이 이 비율보다 더 늘면 불합격입니다.</summary>
    [JsonPropertyName("maxRttP99IncreaseRatio")]
    public double MaxRttP99IncreaseRatio { get; set; } = 0.10;

    /// <summary>
    /// p99.9는 p99보다 흔들림이 크므로 기준을 느슨하게 둡니다.
    /// 여러 회 중앙값을 쓰더라도 남는 변동을 감안한 값입니다.
    /// </summary>
    [JsonPropertyName("maxRttP999IncreaseRatio")]
    public double MaxRttP999IncreaseRatio { get; set; } = 0.25;

    /// <summary>기준 실행 대비 처리량이 이 비율 아래로 떨어지면 불합격입니다.</summary>
    [JsonPropertyName("minThroughputRatio")]
    public double MinThroughputRatio { get; set; } = 0.95;

    /// <summary>기준 실행 대비 메모리 증가가 이 값(MB)을 넘으면 불합격입니다.</summary>
    [JsonPropertyName("maxMemoryGrowthIncreaseMb")]
    public double MaxMemoryGrowthIncreaseMb { get; set; } = 50;

    /// <summary>비교 없이도 지켜야 할 오류율 상한입니다.</summary>
    [JsonPropertyName("maxErrorRate")]
    public double MaxErrorRate { get; set; } = 0.001;

    /// <summary>
    /// 목표 레이트를 이만큼은 달성해야 합니다.
    /// 이 값을 밑돌면 요청한 것보다 가벼운 부하를 건 실행이므로 지연 수치를 신뢰할 수 없습니다.
    /// </summary>
    [JsonPropertyName("minRateAchievement")]
    public double MinRateAchievement { get; set; } = 0.95;

    [JsonPropertyName("requireZeroServerExceptions")]
    public bool RequireZeroServerExceptions { get; set; } = true;

    [JsonPropertyName("requireZeroSessionLeak")]
    public bool RequireZeroSessionLeak { get; set; } = true;

    /// <summary>부하 발생기 자원 고갈이 있으면 그 실행의 수치는 서버 성능을 말해 주지 않습니다.</summary>
    [JsonPropertyName("requireNoLocalResourceExhaustion")]
    public bool RequireNoLocalResourceExhaustion { get; set; } = true;

    public static Thresholds Load(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return new Thresholds();

        if (!File.Exists(path))
            throw new FileNotFoundException($"임계값 파일을 찾을 수 없다: {path}");

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<Thresholds>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        }) ?? new Thresholds();
    }

    public static string ToJsonTemplate()
    {
        return JsonSerializer.Serialize(new Thresholds(), new JsonSerializerOptions { WriteIndented = true });
    }
}
