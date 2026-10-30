using OpenNest.Undo;

namespace OpenNest.Desktop.Tests.Undo;

public class ChangeScopeTests
{
    [Fact]
    public void RootScope_EndsOnceAndCarriesChangesMergedFromNestedScopes()
    {
        var ends = new List<ChangeScope>();
        var root = ChangeScope.Root("Rotate", "key", ends.Add);

        using (var nested = ChangeScope.Nested(root))
        {
            nested.MarkChanged();
            nested.Complete();
            Assert.Equal("Rotate", nested.Label);
            Assert.Null(nested.CoalesceKey);
        }

        Assert.Empty(ends);
        root.Complete();
        root.Dispose();
        root.Dispose();

        var ended = Assert.Single(ends);
        Assert.Same(root, ended);
        Assert.True(ended.IsChanged);
        Assert.True(ended.Succeeded);
        Assert.Equal("key", ended.CoalesceKey);
    }

    [Fact]
    public void IncompleteNestedScope_FailsTheWholeEntry()
    {
        ChangeScope ended = null!;
        var root = ChangeScope.Root("Fill", null, scope => ended = scope);

        using (ChangeScope.Nested(root))
        {
            // Left incomplete, as when the nested edit throws or is canceled.
        }

        root.MarkChanged();
        root.Complete();
        root.Dispose();

        Assert.True(ended.IsChanged);
        Assert.False(ended.Succeeded);
    }

    [Fact]
    public void RootScope_DisposedWithoutComplete_DoesNotSucceed()
    {
        ChangeScope ended = null!;
        using (var root = ChangeScope.Root("Move", null, scope => ended = scope))
            root.MarkChanged();

        Assert.False(ended.Succeeded);
    }

    [Fact]
    public void DetachedScope_RecordsNothingAndToleratesDispose()
    {
        var scope = ChangeScope.Detached();
        Assert.False(scope.IsChanged);

        scope.MarkChanged();
        scope.Complete();
        scope.Dispose();

        Assert.True(scope.IsChanged);
        Assert.Null(scope.Label);
    }
}
