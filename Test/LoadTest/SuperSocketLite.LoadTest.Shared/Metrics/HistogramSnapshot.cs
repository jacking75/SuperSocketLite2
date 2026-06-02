namespace SuperSocketLite.LoadTest.Shared.Metrics;

public readonly record struct HistogramSnapshot(
    long Count,
    long P50Us,
    long P95Us,
    long P99Us,
    long MaxUs);
