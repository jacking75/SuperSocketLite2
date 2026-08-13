namespace SuperSocketLite.LoadTest.Shared.Metrics;

public readonly record struct HistogramSnapshot(
    long Count,
    long P50Us,
    long P90Us,
    long P95Us,
    long P99Us,
    long P999Us,
    long MaxUs);
