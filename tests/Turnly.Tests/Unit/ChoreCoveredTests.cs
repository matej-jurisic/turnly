using Microsoft.EntityFrameworkCore;
using Turnly.Core.Dtos;
using Turnly.Core.Entities;
using Turnly.Core.Enums;
using Turnly.Core.Services;

namespace Turnly.Tests.Unit;

public class ChoreCoveredTests
{
    private static readonly DateTimeOffset Start = new(2026, 6, 17, 9, 0, 0, TimeSpan.Zero);

    private record People(Guid Admin, Guid Ana, Guid Ben, Guid Cy);

    private static async Task<People> SeedAsync(TestContext ctx)
    {
        var admin = await ctx.Setup.CreateFirstAdminAsync(new SetupRequest("admin", "Admin", "password123", null));
        async Task<Guid> Member(string name) =>
            (await ctx.Users.CreateAsync(new CreateUserRequest($"{name.ToLowerInvariant()}-user", name, "password1", UserRole.Member, null))).Value!.Id;
        return new People(admin.Value!.User.Id, await Member("Ana"), await Member("Ben"), await Member("Cy"));
    }

    private static CreateChoreRequest Chore(Guid? current, Guid[] assignees, Guid[][]? pairs = null,
        AssignmentStrategy strategy = AssignmentStrategy.RoundRobin) =>
        new("Garage", null, null, 10, RepeatType.Daily, null, null, null, null, null, null, null, 1, false,
            strategy, SchedulingPreference.FromScheduledDate, null, false, null, Start, assignees, current, null,
            Pairs: pairs);

    private static Task<List<UserNotification>> CoveredAsync(TestContext ctx) =>
        ctx.Db.UserNotifications.Where(n => n.Body.EndsWith("completed this chore for you.")).ToListAsync();

    [Fact]
    public async Task CompleteAsync_tells_the_holder_when_someone_else_does_it()
    {
        using var ctx = new TestContext();
        var p = await SeedAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(Chore(p.Ana, [p.Ana, p.Ben]))).Value!;

        await ctx.Chores.CompleteAsync(chore.Id, p.Ben, new CompleteChoreRequest(null));

        var sent = Assert.Single(await CoveredAsync(ctx));
        Assert.Equal(p.Ana, sent.UserId);
        Assert.Equal(chore.Id, sent.ChoreId);
        Assert.Equal("Ben completed this chore for you.", sent.Body);
    }

    [Fact]
    public async Task CompleteAsync_stays_quiet_when_the_holder_does_it()
    {
        using var ctx = new TestContext();
        var p = await SeedAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(Chore(p.Ana, [p.Ana, p.Ben]))).Value!;

        await ctx.Chores.CompleteAsync(chore.Id, p.Ana, new CompleteChoreRequest(null));

        Assert.Empty(await CoveredAsync(ctx));
    }

    [Fact]
    public async Task CompleteAsync_skips_holders_credited_with_it()
    {
        using var ctx = new TestContext();
        var p = await SeedAsync(ctx);
        // Ana and Ben hold it together; Ben doing it credits Ana as his partner.
        var paired = (await ctx.Chores.CreateAsync(Chore(p.Ana, [p.Ana, p.Ben, p.Cy], [[p.Ana, p.Ben]]))).Value!;
        await ctx.Chores.CompleteAsync(paired.Id, p.Ben, new CompleteChoreRequest(null));
        Assert.Empty(await CoveredAsync(ctx));

        // An admin naming the holder among the people who did it doesn't tell them about it either.
        var solo = (await ctx.Chores.CreateAsync(Chore(p.Cy, [p.Ana, p.Cy]))).Value!;
        await ctx.Chores.CompleteAsync(solo.Id, p.Admin, new CompleteChoreRequest(null, CompletedByUserIds: [p.Ana, p.Cy]));
        Assert.Empty(await CoveredAsync(ctx));
    }

    [Fact]
    public async Task CompleteAsync_tells_both_of_a_pair_and_names_everyone_credited()
    {
        using var ctx = new TestContext();
        var p = await SeedAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(Chore(p.Ana, [p.Ana, p.Ben, p.Cy], [[p.Ana, p.Ben]]))).Value!;

        await ctx.Chores.CompleteAsync(chore.Id, p.Admin, new CompleteChoreRequest(null, CompletedByUserIds: [p.Cy, p.Admin]));

        var sent = await CoveredAsync(ctx);
        Assert.Equal(new[] { p.Ana, p.Ben }.Order(), sent.Select(n => n.UserId).Order());
        Assert.All(sent, n => Assert.Equal("Cy & Admin completed this chore for you.", n.Body));
    }

    [Fact]
    public async Task CompleteAsync_has_nobody_to_tell_on_anyone_and_unassigned_chores()
    {
        using var ctx = new TestContext();
        var p = await SeedAsync(ctx);
        var anyone = (await ctx.Chores.CreateAsync(Chore(null, [p.Ana, p.Ben], strategy: AssignmentStrategy.Anyone))).Value!;
        var manual = (await ctx.Chores.CreateAsync(Chore(null, [p.Ana, p.Ben], strategy: AssignmentStrategy.Manual))).Value!;

        await ctx.Chores.CompleteAsync(anyone.Id, p.Ben, new CompleteChoreRequest(null));
        await ctx.Chores.CompleteAsync(manual.Id, p.Ben, new CompleteChoreRequest(null));

        Assert.Empty(await CoveredAsync(ctx));
    }

    [Fact]
    public async Task SkipAsync_does_not_tell_the_holder()
    {
        using var ctx = new TestContext();
        var p = await SeedAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(Chore(p.Ana, [p.Ana, p.Ben]))).Value!;

        Assert.True((await ctx.Chores.SkipAsync(chore.Id, p.Admin, new SkipChoreRequest(null))).Succeeded);

        Assert.Empty(await CoveredAsync(ctx));
    }
}
