using Arboryn.Application.Abstractions;
using Arboryn.Application.UseCases;
using Arboryn.Domain.Enums;
using Arboryn.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Arboryn.Tests.Unit;

public class PlanUniformizationTests
{
    private const string Root = @"C:\Lib";

    [Fact]
    public void BuildOperations_SkipsAlreadyCanonical()
    {
        var target = @"Livres audio\Asimov\Asimov - Fondation.m4b";
        var instance = Instance(@"C:\Lib\Livres audio\Asimov\Asimov - Fondation.m4b");

        var (ops, alreadyCanonical) = PlanUniformizationHandler.BuildOperations(
            new[] { (instance, target) }, Root, NoneExist);

        ops.Should().BeEmpty();
        alreadyCanonical.Should().Be(1);
    }

    [Fact]
    public void BuildOperations_ProducesMove_WhenDirectoryChanges()
    {
        var instance = Instance(@"C:\Lib\incoming\raw.m4b");

        var (ops, _) = PlanUniformizationHandler.BuildOperations(
            new[] { (instance, @"Livres audio\Asimov\Asimov - Fondation.m4b") }, Root, NoneExist);

        ops.Should().ContainSingle();
        ops[0].Kind.Should().Be(OperationKind.Move);
        ops[0].NewPath.Value.Should().Be(@"C:\Lib\Livres audio\Asimov\Asimov - Fondation.m4b");
    }

    [Fact]
    public void BuildOperations_ProducesRename_WhenOnlyFileNameChanges()
    {
        var instance = Instance(@"C:\Lib\Livres audio\Asimov\old name.m4b");

        var (ops, _) = PlanUniformizationHandler.BuildOperations(
            new[] { (instance, @"Livres audio\Asimov\Asimov - Fondation.m4b") }, Root, NoneExist);

        ops.Should().ContainSingle();
        ops[0].Kind.Should().Be(OperationKind.Rename);
    }

    [Fact]
    public void BuildOperations_DisambiguatesCollidingTargets_WithSuffix()
    {
        // Deux sources distinctes résolvent vers le même nom canonique.
        var a = Instance(@"C:\Lib\in\a.m4b");
        var b = Instance(@"C:\Lib\in\b.m4b");
        const string sameTarget = @"Livres audio\Asimov\Asimov - Fondation.m4b";

        var (ops, _) = PlanUniformizationHandler.BuildOperations(
            new[] { (a, sameTarget), (b, sameTarget) }, Root, NoneExist);

        ops.Should().HaveCount(2);
        ops[0].NewPath.Value.Should().Be(@"C:\Lib\Livres audio\Asimov\Asimov - Fondation.m4b");
        ops[1].NewPath.Value.Should().Be(@"C:\Lib\Livres audio\Asimov\Asimov - Fondation (2).m4b");
    }

    [Fact]
    public void BuildOperations_AvoidsExistingFilesOnDisk()
    {
        var instance = Instance(@"C:\Lib\in\a.m4b");
        var ideal = @"C:\Lib\Livres audio\Asimov\Asimov - Fondation.m4b";
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ideal };

        var (ops, _) = PlanUniformizationHandler.BuildOperations(
            new[] { (instance, @"Livres audio\Asimov\Asimov - Fondation.m4b") },
            Root,
            path => taken.Contains(path.Value));

        ops.Should().ContainSingle();
        ops[0].NewPath.Value.Should().Be(@"C:\Lib\Livres audio\Asimov\Asimov - Fondation (2).m4b");
    }

    private static bool NoneExist(FilePath _) => false;

    private static FileInstanceRecord Instance(string absolutePath)
        => new(
            FileInstanceId.New(),
            VolumeId.Default,
            FilePath.From(absolutePath),
            CanonicalName.From(System.IO.Path.GetFileName(absolutePath)),
            Size: 1000,
            ModifiedAt: DateTime.UnixEpoch);
}
