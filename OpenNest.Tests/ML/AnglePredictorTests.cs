using OpenNest.Engine.ML;

namespace OpenNest.Tests.ML;

// The generic loader is the production session publication path. Reference objects
// isolate its lifetime/concurrency contract; they are not ONNX models or accuracy evidence.
public class AnglePredictorTests
{
    [Fact]
    public void PredictAngles_DefaultThreshold_RemainsPointThree()
    {
        var method = typeof(AnglePredictor).GetMethod(nameof(AnglePredictor.PredictAngles));

        Assert.NotNull(method);
        var threshold = method.GetParameters().Single(parameter => parameter.Name == "threshold");
        Assert.True(threshold.HasDefaultValue);
        Assert.Equal(0.3, threshold.DefaultValue);
    }

    [Fact]
    public void SessionLoader_Success_IsLazyAndReusesSameSessionWithoutReload()
    {
        var attempts = 0;
        var session = new object();
        var loader = new SingleAttemptLoader<object>(() =>
        {
            attempts++;
            return session;
        });

        Assert.Equal(0, attempts);
        for (var i = 0; i < 5; i++)
            Assert.Same(session, loader.GetValue());
        Assert.Equal(1, attempts);
    }

    [Fact]
    public void SessionLoader_MissingModel_RemainsUnavailableWithoutRetry()
    {
        var attempts = 0;
        var modelPresent = false;
        var session = new object();
        var loader = new SingleAttemptLoader<object>(() =>
        {
            attempts++;
            return modelPresent ? session : null!;
        });

        Assert.Null(loader.GetValue());
        modelPresent = true; // Even a subsequently available resource must not trigger a reload.
        for (var i = 0; i < 5; i++)
            Assert.Null(loader.GetValue());
        Assert.Equal(1, attempts);
    }

    [Fact]
    public void SessionLoader_ThrowingLoad_RemainsUnavailableWithoutRetry()
    {
        var attempts = 0;
        var loadFails = true;
        var loader = new SingleAttemptLoader<object>(() =>
        {
            attempts++;
            if (loadFails)
                throw new InvalidDataException("Controlled model-load failure.");
            return new object();
        });

        Assert.Null(loader.GetValue());
        loadFails = false;
        for (var i = 0; i < 5; i++)
            Assert.Null(loader.GetValue());
        Assert.Equal(1, attempts);
    }

    [Fact]
    public void SessionLoaders_AreIndependent_NoGlobalTestSwitchesOrSharedFailures()
    {
        var session = new object();
        var unavailable = new SingleAttemptLoader<object>(() => null!);
        var available = new SingleAttemptLoader<object>(() => session);

        Assert.Null(unavailable.GetValue());
        Assert.Same(session, available.GetValue());
        Assert.Null(unavailable.GetValue());
    }

    [Theory]
    [InlineData("available")]
    [InlineData("missing")]
    [InlineData("throwing")]
    public void SessionLoader_ConcurrentReaders_WaitForDefinitiveAvailability(string outcome)
    {
        using var loadEntered = new ManualResetEventSlim();
        using var releaseLoad = new ManualResetEventSlim();
        var readerEntered = 0;
        var readerCompleted = 0;
        var timeout = TimeSpan.FromSeconds(10);
        var attempts = 0;
        var session = new object();
        var results = new object?[2];
        var errors = new Exception?[2];
        var loader = new SingleAttemptLoader<object>(() =>
        {
            Interlocked.Increment(ref attempts);
            loadEntered.Set();
            releaseLoad.Wait();
            return outcome switch
            {
                "available" => session,
                "missing" => null!,
                _ => throw new InvalidDataException("Controlled model-load failure."),
            };
        });
        var readSession = (int index) =>
        {
            try
            {
                results[index] = loader.GetValue();
            }
            catch (Exception ex)
            {
                errors[index] = ex;
            }
        };
        var initializer = new Thread(() => readSession(0)) { IsBackground = true };
        var reader = new Thread(() =>
        {
            Volatile.Write(ref readerEntered, 1);
            try
            {
                readSession(1);
            }
            finally
            {
                Volatile.Write(ref readerCompleted, 1);
            }
        })
        { IsBackground = true };

        try
        {
            initializer.Start();
            Assert.True(loadEntered.Wait(timeout), "The first caller did not enter the loader.");
            reader.Start();
            Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref readerEntered) == 1, timeout),
                "The concurrent reader did not start.");

            // Observe an actual blocked reader, not just a Task that may not have run yet.
            // The timeout only bounds a broken test; this is not a latency assertion.
            Assert.True(SpinWait.SpinUntil(
                () => Volatile.Read(ref readerCompleted) == 1 || (reader.ThreadState & ThreadState.WaitSleepJoin) != 0,
                timeout), "The reader neither waited nor completed.");
            Assert.False(Volatile.Read(ref readerCompleted) == 1,
                "Availability was published before the blocked load had a definitive result.");
            Assert.Equal(1, Volatile.Read(ref attempts));
        }
        finally
        {
            releaseLoad.Set();
            if ((initializer.ThreadState & ThreadState.Unstarted) == 0)
                Assert.True(initializer.Join(timeout), "The initializing caller did not terminate.");
            if ((reader.ThreadState & ThreadState.Unstarted) == 0)
                Assert.True(reader.Join(timeout), "The concurrent reader did not terminate.");
        }

        Assert.All(errors, error => Assert.Null(error));
        if (outcome == "available")
        {
            Assert.All(results, result => Assert.Same(session, result));
            Assert.Same(session, loader.GetValue());
        }
        else
        {
            Assert.All(results, result => Assert.Null(result));
            Assert.Null(loader.GetValue());
        }
        Assert.Equal(1, attempts);
    }
}
