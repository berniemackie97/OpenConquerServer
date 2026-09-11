using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using OpenConquer.AccountServer.Login.Observability;

namespace OpenConquer.AccountServer.Tests.Login.Observability;

public sealed class LoginRuntimeMetricsTests
{
    private const string MeterName = "OpenConquer.AccountServer.Login";
    private const string SourceRejectionMetricName = "openconquer.account_server.login.admission.source_rejections";

    [Fact]
    public void Constructor_NullMeterFactoryThrows()
    {
        Assert.Throws<ArgumentNullException>(() => new LoginRuntimeMetrics(null!));
    }

    [Fact]
    public void RecordSourceRejection_EmitsDedicatedUnlabeledCounter()
    {
        long recordedValue = 0;
        int measurementCount = 0;

        using MeterListener listener = new();

        listener.InstrumentPublished = (instrument, currentListener) =>
        {
            if (instrument.Meter.Name == MeterName && instrument.Name == SourceRejectionMetricName)
            {
                currentListener.EnableMeasurementEvents(instrument);
            }
        };

        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
        {
            Assert.Equal(SourceRejectionMetricName, instrument.Name);
            Assert.True(tags.IsEmpty);

            Interlocked.Add(ref recordedValue, measurement);
            Interlocked.Increment(ref measurementCount);
        });

        listener.Start();

        using ServiceProvider services = new ServiceCollection()
            .AddMetrics()
            .AddSingleton<LoginRuntimeMetrics>()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        LoginRuntimeMetrics metrics = services.GetRequiredService<LoginRuntimeMetrics>();

        metrics.RecordSourceRejection();

        Assert.Equal(1, Volatile.Read(ref measurementCount));
        Assert.Equal(1, Volatile.Read(ref recordedValue));
    }
}
