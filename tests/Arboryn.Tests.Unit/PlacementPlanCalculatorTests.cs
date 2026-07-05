using Arboryn.Application.Replication;
using Arboryn.Domain.Enums;
using Arboryn.Domain.Replication;
using Arboryn.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Arboryn.Tests.Unit;

public class PlacementPlanCalculatorTests
{
    private readonly PlacementPlanCalculator _calc = new();

    private static VolumeScope Vol(VolumeId id, ScopeExpression scope, VolumeStatus status = VolumeStatus.Online)
        => new(id, id.Value[..4], status, scope);

    private static ReplicaInstance Inst(VolumeId vol, string path, long size = 100)
        => new(FileInstanceId.New(), vol, path, size);

    private static ReplicationCatalogEntry Book(string? canonical, long size, params ReplicaInstance[] instances)
        => new(LogicalFileId.New(), new ScopeSubject(MediaCategory.Book), canonical, size, instances);

    [Fact]
    public void Missing_OnInScopeVolume_PlansCrossVolumeCopy_AndSpaceGrows()
    {
        var a = VolumeId.New();
        var b = VolumeId.New();
        var srcInstance = Inst(a, @"Livres\B\b.epub");
        var entry = Book(@"Livres\B\b.epub", 100, srcInstance);
        var volumes = new[] { Vol(a, ScopeExpression.All), Vol(b, ScopeExpression.All) };

        var plan = _calc.Calculate(new[] { entry }, volumes);

        plan.Operations.Should().ContainSingle();
        var op = plan.Operations[0];
        op.Kind.Should().Be(OperationKind.Copy);
        op.SourceVolumeId.Should().Be(a);
        op.TargetVolumeId.Should().Be(b);
        op.InstanceId.Should().Be(srcInstance.Id);         // instance source (pour exécuter/annuler la copie)
        op.OldRelativePath.Should().Be(@"Livres\B\b.epub"); // chemin de la source
        op.NewRelativePath.Should().Be(@"Livres\B\b.epub");
        op.IsCrossVolume.Should().BeTrue();
        plan.SpaceDeltaByVolume[b].Should().Be(100);
        plan.SpaceDeltaByVolume[a].Should().Be(0);
    }

    [Fact]
    public void PresentAtWrongName_SameDirectory_PlansRename()
    {
        var a = VolumeId.New();
        var entry = Book(@"Dir\new.epub", 100, Inst(a, @"Dir\old.epub"));

        var plan = _calc.Calculate(new[] { entry }, new[] { Vol(a, ScopeExpression.All) });

        plan.Operations.Should().ContainSingle();
        plan.Operations[0].Kind.Should().Be(OperationKind.Rename);
        plan.Operations[0].OldRelativePath.Should().Be(@"Dir\old.epub");
        plan.Operations[0].NewRelativePath.Should().Be(@"Dir\new.epub");
        plan.SpaceDeltaByVolume[a].Should().Be(0);
    }

    [Fact]
    public void PresentInWrongDirectory_PlansMove()
    {
        var a = VolumeId.New();
        var entry = Book(@"Right\f.epub", 100, Inst(a, @"Wrong\f.epub"));

        var plan = _calc.Calculate(new[] { entry }, new[] { Vol(a, ScopeExpression.All) });

        plan.Operations.Should().ContainSingle();
        plan.Operations[0].Kind.Should().Be(OperationKind.Move);
    }

    [Fact]
    public void AlreadyCanonical_PlansNothing()
    {
        var a = VolumeId.New();
        var entry = Book(@"Dir\f.epub", 100, Inst(a, @"Dir\f.epub"));

        var plan = _calc.Calculate(new[] { entry }, new[] { Vol(a, ScopeExpression.All) });

        plan.Operations.Should().BeEmpty();
    }

    [Fact]
    public void MultipleOnSameVolume_KeepsCanonical_DeletesRest_ReclaimingSpace()
    {
        var a = VolumeId.New();
        var canonical = Inst(a, @"Dir\f.epub");
        var dup = Inst(a, @"Dir\f (1).epub");
        var entry = Book(@"Dir\f.epub", 100, canonical, dup);

        var plan = _calc.Calculate(new[] { entry }, new[] { Vol(a, ScopeExpression.All) });

        plan.Operations.Should().ContainSingle(o => o.Kind == OperationKind.Delete);
        var del = plan.Operations.Single(o => o.Kind == OperationKind.Delete);
        del.InstanceId.Should().Be(dup.Id);
        plan.Operations.Should().NotContain(o => o.Kind == OperationKind.Rename || o.Kind == OperationKind.Move);
        plan.SpaceDeltaByVolume[a].Should().Be(-100);
    }

    [Fact]
    public void MultipleOnSameVolume_NoneCanonical_RenamesKeeper_DeletesOther()
    {
        var a = VolumeId.New();
        var keeper = Inst(a, @"Dir\a.epub");   // ordinal-min → keeper
        var other = Inst(a, @"Dir\b.epub");
        var entry = Book(@"Dir\z.epub", 100, other, keeper);

        var plan = _calc.Calculate(new[] { entry }, new[] { Vol(a, ScopeExpression.All) });

        plan.Operations.Should().HaveCount(2);
        plan.Operations.Should().Contain(o =>
            o.Kind == OperationKind.Rename && o.InstanceId == keeper.Id && o.NewRelativePath == @"Dir\z.epub");
        plan.Operations.Should().Contain(o => o.Kind == OperationKind.Delete && o.InstanceId == other.Id);
    }

    [Fact]
    public void OutOfScope_Present_PlansDeletion_AsSurplus()
    {
        var a = VolumeId.New();
        var instance = Inst(a, @"Dir\f.epub");
        var entry = Book(@"Dir\f.epub", 100, instance);
        // volume ne réplique que la vidéo → un livre y est en surplus
        var plan = _calc.Calculate(new[] { entry }, new[] { Vol(a, ScopeExpression.Categories(MediaCategory.Video)) });

        plan.Operations.Should().ContainSingle();
        plan.Operations[0].Kind.Should().Be(OperationKind.Delete);
        plan.Operations[0].InstanceId.Should().Be(instance.Id);
        plan.SpaceDeltaByVolume[a].Should().Be(-100);
    }

    [Fact]
    public void DivergentSizes_ProduceConflict_AndNoOperations()
    {
        var a = VolumeId.New();
        var b = VolumeId.New();
        var entry = Book(@"Dir\f.epub", 100, Inst(a, @"Dir\f.epub", 100), Inst(b, @"Dir\f.epub", 250));
        var volumes = new[] { Vol(a, ScopeExpression.All), Vol(b, ScopeExpression.All) };

        var plan = _calc.Calculate(new[] { entry }, volumes);

        plan.Operations.Should().BeEmpty();
        plan.Conflicts.Should().ContainSingle();
        plan.Conflicts[0].LogicalFileId.Should().Be(entry.LogicalFileId);
        plan.Conflicts[0].Volumes.Should().BeEquivalentTo(new[] { a, b });
        plan.SpaceDeltaByVolume.Values.Should().OnlyContain(v => v == 0);
    }

    [Fact]
    public void InScopeButUnplaceable_IsSkipped_NoCopyPlanned()
    {
        var a = VolumeId.New();
        var b = VolumeId.New();
        var entry = Book(canonical: null, 100, Inst(a, @"somewhere\f.epub"));
        var volumes = new[] { Vol(a, ScopeExpression.All), Vol(b, ScopeExpression.All) };

        var plan = _calc.Calculate(new[] { entry }, volumes);

        plan.Operations.Should().BeEmpty();
        plan.SkippedUnplaceable.Should().Be(1);
    }

    [Fact]
    public void UnplaceableButOutOfScope_StillDeletesSurplus()
    {
        var a = VolumeId.New();
        var entry = Book(canonical: null, 100, Inst(a, @"somewhere\f.epub"));
        // hors scope : la suppression du surplus ne dépend pas du chemin canonique
        var plan = _calc.Calculate(new[] { entry }, new[] { Vol(a, ScopeExpression.None) });

        plan.Operations.Should().ContainSingle(o => o.Kind == OperationKind.Delete);
        plan.SkippedUnplaceable.Should().Be(0);
    }

    [Fact]
    public void CopySource_PrefersOnlineVolume()
    {
        var offline = VolumeId.New();
        var online = VolumeId.New();
        var target = VolumeId.New();
        var entry = Book(@"Dir\f.epub", 100,
            Inst(offline, @"Dir\f.epub"),
            Inst(online, @"Dir\f.epub"));
        var volumes = new[]
        {
            Vol(offline, ScopeExpression.All, VolumeStatus.Offline),
            Vol(online, ScopeExpression.All, VolumeStatus.Online),
            Vol(target, ScopeExpression.All, VolumeStatus.Online),
        };

        var plan = _calc.Calculate(new[] { entry }, volumes);

        var copy = plan.Operations.Should().ContainSingle(o => o.Kind == OperationKind.Copy).Subject;
        copy.SourceVolumeId.Should().Be(online);
        copy.TargetVolumeId.Should().Be(target);
    }

    [Fact]
    public void EmptyInstances_AreIgnored()
    {
        var a = VolumeId.New();
        var entry = Book(@"Dir\f.epub", 100); // aucune instance

        var plan = _calc.Calculate(new[] { entry }, new[] { Vol(a, ScopeExpression.All) });

        plan.Operations.Should().BeEmpty();
        plan.Conflicts.Should().BeEmpty();
    }
}
