using System.Text.Json;
using System.Text.Json.Serialization;
using SuperSocketLite.LoadTest.Shared;

namespace SuperSocketLite.LoadTest.Client.Scenarios;

/// <summary>
/// JSON으로 기술한 시나리오입니다.
/// </summary>
/// <remarks>
/// 시나리오가 C# 클래스로 고정되어 있으면 부하 조합을 바꿀 때마다 빌드를 다시 해야 합니다.
/// 이 형식은 접속 직후 한 번만 보내는 단계(<c>prologue</c>), 그 뒤 비율대로 섞어 보내는
/// 요청 목록(<c>operations</c>), 그리고 요청 간격(<c>thinkTime</c>)을 파일로 기술합니다.
///
/// 기존 <c>--scenario</c> 값들은 그대로 남습니다. 이 파일은 그 자리를 대신할 뿐 없애지 않습니다.
/// </remarks>
public sealed class DeclarativeScenario
{
    private readonly int _totalWeight;

    private DeclarativeScenario(
        string name,
        IReadOnlyList<DeclarativeOperation> prologue,
        IReadOnlyList<DeclarativeOperation> operations,
        TimeSpan? thinkTimeMin,
        TimeSpan? thinkTimeMax)
    {
        Name = name;
        Prologue = prologue;
        Operations = operations;
        ThinkTimeMin = thinkTimeMin;
        ThinkTimeMax = thinkTimeMax;
        _totalWeight = operations.Sum(o => o.Weight);
    }

    public string Name { get; }

    /// <summary>접속할 때마다 순서대로 한 번씩 보내는 요청들입니다. 로그인·방 입장 같은 것입니다.</summary>
    public IReadOnlyList<DeclarativeOperation> Prologue { get; }

    /// <summary>그 뒤로 비율대로 섞어 보내는 요청들입니다.</summary>
    public IReadOnlyList<DeclarativeOperation> Operations { get; }

    /// <summary>요청 간격의 하한입니다. 없으면 <c>--send-rate-per-client</c>로 정합니다.</summary>
    public TimeSpan? ThinkTimeMin { get; }

    public TimeSpan? ThinkTimeMax { get; }

    public static DeclarativeScenario Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"시나리오 파일을 찾지 못했다: {path}", path);

        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch (ArgumentException ex)
        {
            throw new ArgumentException($"{path}: {ex.Message}", ex);
        }
    }

    public static DeclarativeScenario Parse(string json)
    {
        var document = JsonSerializer.Deserialize<ScenarioDocument>(json, SerializerOptions)
            ?? throw new ArgumentException("시나리오 파일이 비어 있다.");

        if (string.IsNullOrWhiteSpace(document.Name))
            throw new ArgumentException("시나리오에 name이 필요하다.");

        var prologue = (document.Prologue ?? []).Select(ToOperation).ToList();
        var operations = (document.Operations ?? []).Select(ToOperation).ToList();

        if (operations.Count == 0)
            throw new ArgumentException("시나리오에 operations가 하나 이상 필요하다.");

        if (operations.Sum(o => o.Weight) <= 0)
            throw new ArgumentException("operations의 weight 합이 0보다 커야 한다.");

        var min = ToTimeSpan(document.ThinkTime?.MinMs);
        var max = ToTimeSpan(document.ThinkTime?.MaxMs);

        if (min is not null && max is not null && min > max)
            throw new ArgumentException("thinkTime.minMs가 maxMs보다 클 수 없다.");

        if ((min is null) != (max is null))
            throw new ArgumentException("thinkTime은 minMs와 maxMs를 함께 적어야 한다.");

        return new DeclarativeScenario(document.Name!, prologue, operations, min, max);
    }

    /// <summary>
    /// 비율에 따라 요청 하나를 고릅니다.
    /// weight가 0인 항목은 뽑히지 않으므로, 특정 요청을 잠시 빼려면 0으로 두면 됩니다.
    /// </summary>
    public DeclarativeOperation PickWeighted(Random random)
    {
        var roll = random.Next(_totalWeight);
        var cumulative = 0;

        foreach (var operation in Operations)
        {
            cumulative += operation.Weight;
            if (roll < cumulative)
                return operation;
        }

        return Operations[^1];
    }

    public TimeSpan NextThinkTime(Random random)
    {
        if (ThinkTimeMin is not { } min || ThinkTimeMax is not { } max)
            throw new InvalidOperationException("This scenario does not define a think time.");

        if (max <= min)
            return min;

        return min + TimeSpan.FromMilliseconds(random.NextDouble() * (max - min).TotalMilliseconds);
    }

    private static DeclarativeOperation ToOperation(OperationDocument document)
    {
        if (string.IsNullOrWhiteSpace(document.Type))
            throw new ArgumentException("각 요청에 type이 필요하다.");

        if (document.PacketId is < short.MinValue or > short.MaxValue)
            throw new ArgumentException($"'{document.Type}'의 packetId가 Int16 범위를 벗어났다.");

        if (document.Weight < 0)
            throw new ArgumentException($"'{document.Type}'의 weight는 음수일 수 없다.");

        if (document.PayloadBytes is { } bytes && (bytes < 0 || bytes > PayloadFactory.MaxBodySize))
            throw new ArgumentException(
                $"'{document.Type}'의 payloadBytes는 0 이상 {PayloadFactory.MaxBodySize} 이하여야 한다. 헤더의 길이 필드가 Int16이기 때문이다.");

        return new DeclarativeOperation(
            document.Type!,
            (short)document.PacketId,
            document.Weight,
            document.Payload ?? "small",
            document.PayloadBytes);
    }

    private static TimeSpan? ToTimeSpan(int? milliseconds)
    {
        if (milliseconds is not { } value)
            return null;

        if (value < 0)
            throw new ArgumentException("thinkTime은 음수일 수 없다.");

        return TimeSpan.FromMilliseconds(value);
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private sealed class ScenarioDocument
    {
        public string? Name { get; set; }
        public ThinkTimeDocument? ThinkTime { get; set; }
        public List<OperationDocument>? Prologue { get; set; }
        public List<OperationDocument>? Operations { get; set; }
    }

    private sealed class ThinkTimeDocument
    {
        public int? MinMs { get; set; }
        public int? MaxMs { get; set; }
    }

    private sealed class OperationDocument
    {
        public string? Type { get; set; }
        public int PacketId { get; set; }
        public int Weight { get; set; }
        public string? Payload { get; set; }

        [JsonPropertyName("payloadBytes")]
        public int? PayloadBytes { get; set; }
    }
}

/// <summary>선언적 시나리오의 요청 하나입니다.</summary>
/// <param name="Type">CSV의 operation_type 으로 남는 이름입니다.</param>
/// <param name="PacketId">보낼 패킷 ID입니다.</param>
/// <param name="Weight">뽑힐 비율입니다. prologue 항목에서는 쓰이지 않습니다.</param>
/// <param name="Payload">본문 크기 프로필입니다. small · medium · large · huge · mixed · mixed-huge.</param>
/// <param name="PayloadBytes">본문 크기를 바이트로 직접 정할 때 씁니다. 있으면 <paramref name="Payload"/>보다 우선합니다.</param>
public sealed record DeclarativeOperation(
    string Type,
    short PacketId,
    int Weight,
    string Payload,
    int? PayloadBytes)
{
    public BinaryPacket CreatePacket(int clientId, int sequence)
    {
        var body = PayloadBytes is { } bytes
            ? PayloadFactory.CreateExact(clientId, sequence, bytes)
            : PayloadFactory.Create(clientId, sequence, Payload);

        return new BinaryPacket(PacketId, 0, body);
    }
}

/// <summary>
/// 클라이언트 하나가 선언적 시나리오를 진행하는 상태입니다.
/// prologue 는 접속마다 다시 시작합니다. 재접속 후에도 로그인부터 다시 해야 하기 때문입니다.
/// </summary>
public sealed class DeclarativeScenarioRunner
{
    private readonly DeclarativeScenario _scenario;
    private int _prologueIndex;

    public DeclarativeScenarioRunner(DeclarativeScenario scenario)
    {
        _scenario = scenario;
    }

    public void Reset() => _prologueIndex = 0;

    public DeclarativeOperation Next(Random random)
    {
        if (_prologueIndex < _scenario.Prologue.Count)
            return _scenario.Prologue[_prologueIndex++];

        return _scenario.PickWeighted(random);
    }
}
