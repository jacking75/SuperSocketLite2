using System.Diagnostics.Metrics;

namespace SuperSocketLite.LoadTest.ServerProbe;

/// <summary>
/// 한 번 읽은 SuperSocketLite 런타임 게이지 값입니다.
/// 계기를 관측하지 못한 항목은 -1입니다. 큐가 빈 것(0)과 계측이 없는 것을 구분하기 위해서입니다.
/// </summary>
public readonly record struct RuntimeGaugeValues(
    int SendQueueDepthTotal,
    int SendQueueDepthMax,
    int ReceivePoolAvailable,
    int ReceivePoolTotal,
    int SendPoolAvailable,
    int SendPoolTotal)
{
    public static RuntimeGaugeValues Unavailable { get; } = new(-1, -1, -1, -1, -1, -1);
}

/// <summary>
/// SuperSocketLite Meter의 런타임 게이지를 읽습니다.
/// 라이브러리는 송신 큐 깊이와 SAEA 풀 잔량을 공개 속성이 아니라 계기로만 내보내므로
/// <see cref="MeterListener"/>로 받습니다.
/// </summary>
/// <remarks>
/// 구독은 게이지 여섯 개로 한정합니다.
/// 같은 Meter에는 요청마다 값을 더하는 카운터도 있어서, 그것까지 구독하면
/// 측정 대상의 송수신 경로에 콜백 비용이 얹힙니다.
/// </remarks>
public sealed class RuntimeGaugeReader : IDisposable
{
    public const string MeterName = "SuperSocketLite";

    private const string SendQueueDepthTotalName = "send-queue-depth-total";
    private const string SendQueueDepthMaxName = "send-queue-depth-max";
    private const string ReceivePoolAvailableName = "receive-saea-pool-available";
    private const string ReceivePoolTotalName = "receive-saea-pool-total";
    private const string SendPoolAvailableName = "send-saea-pool-available";
    private const string SendPoolTotalName = "send-saea-pool-total";

    private static readonly string[] TrackedInstruments =
    [
        SendQueueDepthTotalName,
        SendQueueDepthMaxName,
        ReceivePoolAvailableName,
        ReceivePoolTotalName,
        SendPoolAvailableName,
        SendPoolTotalName
    ];

    private readonly MeterListener? _listener;
    private readonly object _gate = new();

    private int _sendQueueDepthTotal = -1;
    private int _sendQueueDepthMax = -1;
    private int _receivePoolAvailable = -1;
    private int _receivePoolTotal = -1;
    private int _sendPoolAvailable = -1;
    private int _sendPoolTotal = -1;

    private RuntimeGaugeReader(MeterListener? listener)
    {
        _listener = listener;
    }

    /// <summary>
    /// 게이지 구독을 시작합니다.
    /// <paramref name="enabled"/>가 false면 아무것도 구독하지 않고 항상 <see cref="RuntimeGaugeValues.Unavailable"/>를 돌려줍니다.
    /// 계측 오버헤드를 재는 실행에서 이 경로를 씁니다.
    /// </summary>
    public static RuntimeGaugeReader Create(bool enabled)
    {
        if (!enabled)
            return new RuntimeGaugeReader(null);

        var reader = new RuntimeGaugeReader(new MeterListener());
        reader.StartListening();
        return reader;
    }

    private void StartListening()
    {
        var listener = _listener!;

        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == MeterName && Array.IndexOf(TrackedInstruments, instrument.Name) >= 0)
                l.EnableMeasurementEvents(instrument);
        };

        listener.SetMeasurementEventCallback<int>(OnMeasurement);
        listener.Start();
    }

    /// <summary>
    /// 게이지를 지금 관측해 값을 돌려줍니다.
    /// 서버 인스턴스가 여러 개면(바이너리·text-line) 같은 계기가 여러 번 보고되므로 합산합니다.
    /// 최대 대기량만 합이 아니라 최댓값을 취합니다.
    /// </summary>
    public RuntimeGaugeValues Read()
    {
        if (_listener is null)
            return RuntimeGaugeValues.Unavailable;

        lock (_gate)
        {
            _sendQueueDepthTotal = -1;
            _sendQueueDepthMax = -1;
            _receivePoolAvailable = -1;
            _receivePoolTotal = -1;
            _sendPoolAvailable = -1;
            _sendPoolTotal = -1;

            // 콜백은 이 호출 안에서 같은 스레드로 동기 실행된다.
            _listener.RecordObservableInstruments();

            return new RuntimeGaugeValues(
                _sendQueueDepthTotal,
                _sendQueueDepthMax,
                _receivePoolAvailable,
                _receivePoolTotal,
                _sendPoolAvailable,
                _sendPoolTotal);
        }
    }

    private void OnMeasurement(
        Instrument instrument,
        int measurement,
        ReadOnlySpan<KeyValuePair<string, object?>> tags,
        object? state)
    {
        switch (instrument.Name)
        {
            case SendQueueDepthTotalName:
                Accumulate(ref _sendQueueDepthTotal, measurement);
                break;
            case SendQueueDepthMaxName:
                _sendQueueDepthMax = Math.Max(_sendQueueDepthMax, measurement);
                break;
            case ReceivePoolAvailableName:
                Accumulate(ref _receivePoolAvailable, measurement);
                break;
            case ReceivePoolTotalName:
                Accumulate(ref _receivePoolTotal, measurement);
                break;
            case SendPoolAvailableName:
                Accumulate(ref _sendPoolAvailable, measurement);
                break;
            case SendPoolTotalName:
                Accumulate(ref _sendPoolTotal, measurement);
                break;
        }
    }

    // -1(미관측)에서 시작하므로 첫 측정에서 0으로 내린 뒤 더한다.
    private static void Accumulate(ref int field, int measurement)
    {
        field = (field < 0 ? 0 : field) + measurement;
    }

    public void Dispose()
    {
        _listener?.Dispose();
    }
}
