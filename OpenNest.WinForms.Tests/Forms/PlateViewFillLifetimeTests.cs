using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using OpenNest.CNC;
using OpenNest.Controls;
using OpenNest.Engine;
using OpenNest.Engine.Strategies;
using OpenNest.Forms;
using OpenNest.Geometry;

namespace OpenNest.WinForms.Tests.Forms;

[CollectionDefinition("Fill operation lifetime", DisableParallelization = true)]
public class FillOperationLifetimeCollection;

[Collection("Fill operation lifetime")]
public class PlateViewFillLifetimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClosedProgressWindowStaysBusyThroughCommitAndCleanup(bool accept)
    {
        RunSta(() =>
        {
            using var run = new FillRun();
            run.Start();
            var progressForm = Assert.Single(Application.OpenForms.OfType<NestProgressForm>());
            if (accept)
                Invoke(progressForm, "AcceptButton_Click", null, EventArgs.Empty);
            progressForm.Close();

            Assert.Empty(Application.OpenForms.OfType<NestProgressForm>());
            Assert.True(run.View.IsFillInProgress);
            Assert.True(run.Context.Token.IsCancellationRequested);

            // Closing the modeless window must neither release the guard nor allow another fill.
            run.View.FillWithProgress(run.Group, run.WorkArea);
            Assert.Empty(Application.OpenForms.OfType<NestProgressForm>());
            Assert.Equal(1, run.Calls);

            using (var dialog = new AutomaticCutOffForm(run.View, Units.Inches))
            {
                Invoke(dialog, "PreviewButton_Click", null, EventArgs.Empty);
                Assert.Empty(PreviewParts(run.View));
                Assert.False(dialog.Controls.Find("applyButton", true).Single().Enabled);
                Invoke(dialog, "ApplyButton_Click", null, EventArgs.Empty);
                Assert.NotEqual(DialogResult.OK, dialog.DialogResult);
                Assert.Empty(run.View.Plate.CutOffs);
            }

            var busyDuringCommit = false;
            run.View.Plate.PartAdded += (_, _) => busyDuringCommit = run.View.IsFillInProgress;
            run.Complete();

            Assert.Equal(accept, busyDuringCommit);
            Assert.Equal(accept ? 2 : 1, run.View.Plate.Parts.Count);
            Assert.False(run.View.IsFillInProgress);
            Assert.Null(run.View.ActiveWorkArea);
            Assert.Empty(PreviewParts(run.View));
            Assert.Empty(Application.OpenForms.OfType<NestProgressForm>());
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QueuedProgressCannotOverwriteNewPreviewEvenDuringAnotherFill(bool startAnotherFill)
    {
        RunSta(() =>
        {
            using var run = new FillRun();
            run.Start();
            run.Complete();
            var lateCallbacks = run.Dispatcher.TakeProgress();
            Assert.NotEmpty(lateCallbacks);

            using var dialog = new AutomaticCutOffForm(run.View, Units.Inches);
            Invoke(dialog, "PreviewButton_Click", null, EventArgs.Empty);
            var preview = PreviewParts(run.View).ToArray();
            Assert.NotEmpty(preview);

            if (startAnotherFill)
                run.Start();

            foreach (var callback in lateCallbacks)
                callback();

            Assert.Equal(preview, PreviewParts(run.View).ToArray());
            Assert.Null(run.View.ActiveWorkArea);
            Assert.Equal(startAnotherFill, run.View.IsFillInProgress);
        });
    }

    [Fact]
    public void SetupFailureReleasesGuardAndClosesProgressWindow()
    {
        RunSta(() =>
        {
            using var run = new FillRun();
            var plateField = typeof(PlateView).GetField("plate", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var plate = run.View.Plate;
            // Inject an unavailable input without invoking SetPlate's non-null contract.
            plateField.SetValue(run.View, null);
            try
            {
                run.View.FillWithProgress(run.Group, run.WorkArea);
                Assert.False(run.View.IsFillInProgress);
                Assert.Empty(Application.OpenForms.OfType<NestProgressForm>());
            }
            finally
            {
                plateField.SetValue(run.View, plate);
            }

            run.Start();
            run.Complete();
            Assert.False(run.View.IsFillInProgress);
        });
    }

    [Fact]
    public void WorkerFailureReleasesGuardAndDiscardsPreview()
    {
        RunSta(() =>
        {
            using var run = new FillRun { Fail = true };
            run.Start();
            run.Complete();
            Assert.False(run.View.IsFillInProgress);
            Assert.Empty(PreviewParts(run.View));
            Assert.Single(run.View.Plate.Parts);
            Assert.Empty(Application.OpenForms.OfType<NestProgressForm>());
        });
    }

    // Use the real strategy registry rather than a production-only injection hook.
    public sealed class BlockingFillStrategy : IFillStrategy
    {
        public string Name => "WinForms lifetime regression";
        public NestPhase Phase => NestPhase.Linear;
        public int Order => 0;
        public Func<FillContext, List<Part>>? Run { get; set; }
        public List<Part> Fill(FillContext context) => Run!(context);
    }

    private sealed class FillRun : IDisposable
    {
        private readonly ManualResetEventSlim entered = new();
        private readonly ManualResetEventSlim release = new();
        private readonly BlockingFillStrategy strategy;
        private readonly string[] priorStrategies;
        private readonly string priorEngine;
        private readonly SynchronizationContext? priorContext;

        public PlateView View { get; }
        public List<Part> Group { get; }
        public Box WorkArea { get; }
        public ControlledDispatcher Dispatcher { get; } = new();
        public FillContext Context { get; private set; } = null!;
        public bool Fail { get; init; }
        public int Calls { get; private set; }

        public FillRun()
        {
            priorStrategies = FillStrategyRegistry.Strategies.Select(s => s.Name).ToArray();
            priorEngine = EngineSelection.EngineName;
            FillStrategyRegistry.LoadFrom(typeof(BlockingFillStrategy).Assembly);
            strategy = Assert.Single(FillStrategyRegistry.AllStrategies.OfType<BlockingFillStrategy>());
            FillStrategyRegistry.Disable(strategy.Name);
            FillStrategyRegistry.SetEnabled(strategy.Name);
            EngineSelection.EngineName = EngineSelection.DefaultEngineName;
            strategy.Run = Fill;

            var program = new CNC.Program();
            program.Codes.Add(new RapidMove(0, 0));
            program.Codes.Add(new LinearMove(10, 0));
            program.Codes.Add(new LinearMove(10, 5));
            program.Codes.Add(new LinearMove(0, 5));
            program.Codes.Add(new LinearMove(0, 0));
            var drawing = new Drawing("fill lifetime rectangle", program);
            var plate = new Plate(36, 120) { PartSpacing = 0.5 };
            plate.Parts.Add(new Part(drawing, new Vector(70, 10)));
            View = new PlateView { Plate = plate };
            Group = [new Part(drawing)];
            WorkArea = new Box(0, 0, 60, 36);
            priorContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(Dispatcher);
        }

        private List<Part> Fill(FillContext context)
        {
            Calls++;
            Context = context;
            var parts = new List<Part> { new(context.Item.Drawing, new Vector(1, 1)) };
            context.ReportProgress(parts, "Blocked until the test releases the worker");
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(15)))
                throw new TimeoutException("The test did not release the fill worker.");
            if (Fail)
                throw new InvalidOperationException("Injected worker failure");
            return parts;
        }

        public void Start()
        {
            entered.Reset();
            release.Reset();
            View.FillWithProgress(Group, WorkArea);
            Assert.True(entered.Wait(TimeSpan.FromSeconds(15)), "The fill strategy was not entered.");
            Assert.True(View.IsFillInProgress);
        }

        public void Complete()
        {
            release.Set();
            Dispatcher.CompleteOperations();
        }

        public void Dispose()
        {
            try
            {
                Complete();
            }
            finally
            {
                strategy.Run = null;
                FillStrategyRegistry.SetEnabled(priorStrategies);
                EngineSelection.EngineName = priorEngine;
                SynchronizationContext.SetSynchronizationContext(priorContext);
                View.Dispose();
                entered.Dispose();
                release.Dispose();
            }
        }
    }

    // Hold Progress<T> deliveries but execute await continuations on the STA. No sleeps,
    // message-loop polling, or dependence on which thread wins the completion/progress race.
    private sealed class ControlledDispatcher : SynchronizationContext
    {
        private readonly BlockingCollection<System.Action> continuations = new();
        private readonly ConcurrentQueue<System.Action> progress = new();
        private int operations;

        public override void OperationStarted() => operations++;
        public override void OperationCompleted() => operations--;

        public override void Post(SendOrPostCallback callback, object? state)
        {
            if (state is NestProgress)
                progress.Enqueue(() => callback(state));
            else
                continuations.Add(() => callback(state));
        }

        public System.Action[] TakeProgress()
        {
            var callbacks = new List<System.Action>();
            while (progress.TryDequeue(out var callback))
                callbacks.Add(callback);
            return callbacks.ToArray();
        }

        public void CompleteOperations()
        {
            while (operations > 0)
            {
                Assert.True(continuations.TryTake(out var callback, TimeSpan.FromSeconds(15)),
                    "The fill did not complete.");
                callback!();
            }
        }
    }

    private static IReadOnlyList<LayoutPart> PreviewParts(PlateView view) =>
        (IReadOnlyList<LayoutPart>)typeof(PlateView)
            .GetProperty("PreviewParts", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)!;

    private static void Invoke(Form form, string method, params object?[] arguments) =>
        form.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(form, arguments);

    private static void RunSta(System.Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        })
        { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "The STA test did not complete.");
        if (failure != null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
