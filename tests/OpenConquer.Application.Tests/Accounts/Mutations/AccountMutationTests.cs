using OpenConquer.Application.Accounts.Authentication;
using OpenConquer.Application.Accounts.Mutations;
using OpenConquer.Domain.Accounts;

namespace OpenConquer.Application.Tests.Accounts.Mutations;

public sealed class AccountMutationTests
{
    private static readonly Guid s_correlationId = Guid.Parse("9bfbdcfb-433f-4b13-a445-e5031831697d");

    [Fact]
    public void Context_ForSystemExposesExpectedValues()
    {
        AccountMutationContext context = AccountMutationContext.ForSystem(s_correlationId, "moderation");

        Assert.Equal(AccountActorKind.System, context.ActorKind);
        Assert.Equal(s_correlationId, context.CorrelationId);
        Assert.Equal("moderation", context.ReasonCode);
    }

    [Fact]
    public void Context_ForMigrationExposesExpectedValues()
    {
        AccountMutationContext context = AccountMutationContext.ForMigration(s_correlationId);

        Assert.Equal(AccountActorKind.Migration, context.ActorKind);
        Assert.Equal(s_correlationId, context.CorrelationId);
        Assert.Null(context.ReasonCode);
    }

    [Fact]
    public void Context_RejectsEmptyCorrelationId()
    {
        Assert.Throws<ArgumentException>(() => AccountMutationContext.ForSystem(Guid.Empty));
        Assert.Throws<ArgumentException>(() => AccountMutationContext.ForMigration(Guid.Empty));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("bad\nreason")]
    [InlineData("bad\treason")]
    [InlineData("non-ascii-é")]
    public void Context_RejectsInvalidReasonCode(string reasonCode)
    {
        Assert.Throws<ArgumentException>(() => AccountMutationContext.ForSystem(s_correlationId, reasonCode));
    }

    [Fact]
    public void Context_RejectsOversizedReasonCode()
    {
        string reasonCode = new('a', AccountAuditPolicy.MaximumReasonCodeLength + 1);

        Assert.Throws<ArgumentException>(() => AccountMutationContext.ForSystem(s_correlationId, reasonCode));
    }

    [Fact]
    public void Context_AcceptsMaximumReasonCodeLength()
    {
        string reasonCode = new('a', AccountAuditPolicy.MaximumReasonCodeLength);

        AccountMutationContext context = AccountMutationContext.ForSystem(s_correlationId, reasonCode);

        Assert.Equal(reasonCode, context.ReasonCode);
    }

    [Fact]
    public void Constructor_RejectsNullDependencies()
    {
        Assert.Throws<ArgumentNullException>(() => new AccountMutationService(null!, new FakePasswordHasher()));
        Assert.Throws<ArgumentNullException>(() => new AccountMutationService(new FakeMutationStore(), null!));
    }

    [Fact]
    public async Task ChangeAccessStatus_ForwardsValidatedMutation()
    {
        FakeMutationStore store = new() { Result = AccountMutationStatus.Applied };
        AccountMutationService service = new(store, new FakePasswordHasher());
        AccountMutationContext context = AccountMutationContext.ForSystem(s_correlationId, "moderation");

        AccountMutationStatus result = await service.ChangeAccessStatusAsync(42, AccountAccessStatus.Banned, 7, context, TestContext.Current.CancellationToken);

        Assert.Equal(AccountMutationStatus.Applied, result);
        Assert.Equal("ChangeAccessStatus", store.Operation);
        Assert.Equal(42u, store.AccountId);
        Assert.Equal(7ul, store.ExpectedStateRevision);
        Assert.Equal(AccountAccessStatus.Banned, store.AccessStatus);
        Assert.Same(context, store.Context);
    }

    [Fact]
    public async Task ChangeAccessStatus_RejectsUndefinedStatus()
    {
        FakeMutationStore store = new();
        AccountMutationService service = new(store, new FakePasswordHasher());
        AccountMutationContext context = AccountMutationContext.ForSystem(s_correlationId);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.ChangeAccessStatusAsync(42, (AccountAccessStatus)255, 7, context, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(0, store.CallCount);
    }

    [Fact]
    public async Task ChangeAuthorityRole_ForwardsValidatedMutation()
    {
        FakeMutationStore store = new();
        AccountMutationService service = new(store, new FakePasswordHasher());
        AccountMutationContext context = AccountMutationContext.ForMigration(s_correlationId);

        AccountMutationStatus result = await service.ChangeAuthorityRoleAsync(42, AccountAuthorityRole.Gm, 7, context, TestContext.Current.CancellationToken);

        Assert.Equal(AccountMutationStatus.Applied, result);
        Assert.Equal("ChangeAuthorityRole", store.Operation);
        Assert.Equal(AccountAuthorityRole.Gm, store.AuthorityRole);
    }

    [Fact]
    public async Task ChangeAuthorityRole_RejectsUndefinedRole()
    {
        FakeMutationStore store = new();
        AccountMutationService service = new(store, new FakePasswordHasher());
        AccountMutationContext context = AccountMutationContext.ForSystem(s_correlationId);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.ChangeAuthorityRoleAsync(42, (AccountAuthorityRole)255, 7, context, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(0, store.CallCount);
    }

    [Fact]
    public async Task DeleteAccount_ForwardsValidatedMutation()
    {
        FakeMutationStore store = new();
        AccountMutationService service = new(store, new FakePasswordHasher());
        AccountMutationContext context = AccountMutationContext.ForSystem(s_correlationId);

        AccountMutationStatus result = await service.DeleteAccountAsync(42, 7, context, TestContext.Current.CancellationToken);

        Assert.Equal(AccountMutationStatus.Applied, result);
        Assert.Equal("DeleteAccount", store.Operation);
        Assert.Equal(42u, store.AccountId);
        Assert.Equal(7ul, store.ExpectedStateRevision);
    }

    [Fact]
    public async Task RestoreAccount_ForwardsValidatedMutation()
    {
        FakeMutationStore store = new();
        AccountMutationService service = new(store, new FakePasswordHasher());
        AccountMutationContext context = AccountMutationContext.ForSystem(s_correlationId);

        AccountMutationStatus result = await service.RestoreAccountAsync(42, 7, context, TestContext.Current.CancellationToken);

        Assert.Equal(AccountMutationStatus.Applied, result);
        Assert.Equal("RestoreAccount", store.Operation);
    }

    [Fact]
    public async Task Mutation_RejectsZeroAccountId()
    {
        FakeMutationStore store = new();
        AccountMutationService service = new(store, new FakePasswordHasher());
        AccountMutationContext context = AccountMutationContext.ForSystem(s_correlationId);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.DeleteAccountAsync(0, 7, context, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(0, store.CallCount);
    }

    [Fact]
    public async Task Mutation_RejectsZeroStateRevision()
    {
        FakeMutationStore store = new();
        AccountMutationService service = new(store, new FakePasswordHasher());
        AccountMutationContext context = AccountMutationContext.ForSystem(s_correlationId);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.DeleteAccountAsync(42, 0, context, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(0, store.CallCount);
    }

    [Fact]
    public async Task Mutation_RejectsNullContext()
    {
        FakeMutationStore store = new();
        AccountMutationService service = new(store, new FakePasswordHasher());

        await Assert.ThrowsAsync<ArgumentNullException>(() => service.DeleteAccountAsync(42, 7, null!, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(0, store.CallCount);
    }

    [Fact]
    public async Task Mutation_ObservesCancellationBeforePersistence()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        FakeMutationStore store = new();
        AccountMutationService service = new(store, new FakePasswordHasher());
        AccountMutationContext context = AccountMutationContext.ForSystem(s_correlationId);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.DeleteAccountAsync(42, 7, context, cancellation.Token).AsTask());

        Assert.Equal(0, store.CallCount);
    }

    [Fact]
    public async Task ResetPassword_SystemActorHashesAndForwardsPassword()
    {
        FakeMutationStore store = new();
        FakePasswordHasher hasher = new();
        AccountMutationService service = new(store, hasher);
        AccountMutationContext context = AccountMutationContext.ForSystem(s_correlationId, "credential-reset");

        AccountMutationStatus result = await service.ResetPasswordAsync(42, "NewSecret".AsMemory(), 7, 11, context, TestContext.Current.CancellationToken);

        Assert.Equal(AccountMutationStatus.Applied, result);
        Assert.Equal(1, hasher.HashCount);
        Assert.Equal("NewSecret", hasher.LastPassword);
        Assert.Equal("ResetPassword", store.Operation);
        Assert.Equal("$test$new-password-hash$", store.PasswordHash);
        Assert.Equal(11ul, store.ExpectedPasswordCredentialRevision);
        Assert.Same(context, store.Context);
    }

    [Fact]
    public async Task ResetPassword_MigrationActorIsRejected()
    {
        FakeMutationStore store = new();
        FakePasswordHasher hasher = new();
        AccountMutationService service = new(store, hasher);
        AccountMutationContext context = AccountMutationContext.ForMigration(s_correlationId);

        await Assert.ThrowsAsync<ArgumentException>(() => service.ResetPasswordAsync(42, "NewSecret".AsMemory(), 7, 11, context, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(0, hasher.HashCount);
        Assert.Equal(0, store.CallCount);
    }

    [Fact]
    public async Task ResetPassword_RejectsZeroCredentialRevision()
    {
        FakeMutationStore store = new();
        FakePasswordHasher hasher = new();
        AccountMutationService service = new(store, hasher);
        AccountMutationContext context = AccountMutationContext.ForSystem(s_correlationId);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.ResetPasswordAsync(42, "NewSecret".AsMemory(), 7, 0, context, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(0, hasher.HashCount);
        Assert.Equal(0, store.CallCount);
    }

    [Fact]
    public async Task ResetPassword_RejectsInvalidPasswordBeforeHashing()
    {
        FakeMutationStore store = new();
        FakePasswordHasher hasher = new();
        AccountMutationService service = new(store, hasher);
        AccountMutationContext context = AccountMutationContext.ForSystem(s_correlationId);

        await Assert.ThrowsAsync<ArgumentException>(() => service.ResetPasswordAsync(42, ReadOnlyMemory<char>.Empty, 7, 11, context, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(0, hasher.HashCount);
        Assert.Equal(0, store.CallCount);
    }

    [Fact]
    public async Task ResetPassword_RejectsInvalidHasherResult()
    {
        FakeMutationStore store = new();
        FakePasswordHasher hasher = new() { Result = string.Empty };
        AccountMutationService service = new(store, hasher);
        AccountMutationContext context = AccountMutationContext.ForSystem(s_correlationId);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ResetPasswordAsync(42, "NewSecret".AsMemory(), 7, 11, context, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(1, hasher.HashCount);
        Assert.Equal(0, store.CallCount);
    }

    [Fact]
    public async Task ResetPassword_CancellationAfterHashingPreventsPersistence()
    {
        using CancellationTokenSource cancellation = new();

        FakeMutationStore store = new();
        FakePasswordHasher hasher = new() { OnHash = cancellation.Cancel };
        AccountMutationService service = new(store, hasher);
        AccountMutationContext context = AccountMutationContext.ForSystem(s_correlationId);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ResetPasswordAsync(42, "NewSecret".AsMemory(), 7, 11, context, cancellation.Token).AsTask());

        Assert.Equal(1, hasher.HashCount);
        Assert.Equal(0, store.CallCount);
    }

    [Fact]
    public async Task StoreStatusPropagatesWithoutTranslation()
    {
        FakeMutationStore store = new() { Result = AccountMutationStatus.StateConflict };
        AccountMutationService service = new(store, new FakePasswordHasher());
        AccountMutationContext context = AccountMutationContext.ForSystem(s_correlationId);

        AccountMutationStatus result = await service.DeleteAccountAsync(42, 7, context, TestContext.Current.CancellationToken);

        Assert.Equal(AccountMutationStatus.StateConflict, result);
    }

    private sealed class FakePasswordHasher : IAccountPasswordHasher
    {
        public string Result { get; init; } = "$test$new-password-hash$";
        public Action? OnHash { get; init; }
        public int HashCount { get; private set; }
        public string? LastPassword { get; private set; }

        public AccountPasswordVerificationStatus VerifyPassword(string passwordHash, ReadOnlySpan<char> password) => throw new NotSupportedException();

        public void VerifyDecoy(ReadOnlySpan<char> password) => throw new NotSupportedException();

        public string HashPassword(ReadOnlySpan<char> password)
        {
            HashCount++;
            LastPassword = password.ToString();
            OnHash?.Invoke();

            return Result;
        }
    }

    private sealed class FakeMutationStore : IAccountMutationStore
    {
        public AccountMutationStatus Result { get; init; } = AccountMutationStatus.Applied;
        public int CallCount { get; private set; }
        public string? Operation { get; private set; }
        public uint AccountId { get; private set; }
        public ulong ExpectedStateRevision { get; private set; }
        public ulong ExpectedPasswordCredentialRevision { get; private set; }
        public AccountAccessStatus? AccessStatus { get; private set; }
        public AccountAuthorityRole? AuthorityRole { get; private set; }
        public string? PasswordHash { get; private set; }
        public AccountMutationContext? Context { get; private set; }

        public ValueTask<AccountMutationStatus> ChangeAccessStatusAsync(uint accountId, AccountAccessStatus newAccessStatus, ulong expectedStateRevision, AccountMutationContext context, CancellationToken cancellationToken = default)
        {
            Capture("ChangeAccessStatus", accountId, expectedStateRevision, context);
            AccessStatus = newAccessStatus;
            return ValueTask.FromResult(Result);
        }

        public ValueTask<AccountMutationStatus> ChangeAuthorityRoleAsync(uint accountId, AccountAuthorityRole newAuthorityRole, ulong expectedStateRevision, AccountMutationContext context, CancellationToken cancellationToken = default)
        {
            Capture("ChangeAuthorityRole", accountId, expectedStateRevision, context);
            AuthorityRole = newAuthorityRole;
            return ValueTask.FromResult(Result);
        }

        public ValueTask<AccountMutationStatus> DeleteAccountAsync(uint accountId, ulong expectedStateRevision, AccountMutationContext context, CancellationToken cancellationToken = default)
        {
            Capture("DeleteAccount", accountId, expectedStateRevision, context);
            return ValueTask.FromResult(Result);
        }

        public ValueTask<AccountMutationStatus> RestoreAccountAsync(uint accountId, ulong expectedStateRevision, AccountMutationContext context, CancellationToken cancellationToken = default)
        {
            Capture("RestoreAccount", accountId, expectedStateRevision, context);
            return ValueTask.FromResult(Result);
        }

        public ValueTask<AccountMutationStatus> ResetPasswordAsync(uint accountId, string newPasswordHash, ulong expectedStateRevision, ulong expectedPasswordCredentialRevision, AccountMutationContext context, CancellationToken cancellationToken = default)
        {
            Capture("ResetPassword", accountId, expectedStateRevision, context);
            PasswordHash = newPasswordHash;
            ExpectedPasswordCredentialRevision = expectedPasswordCredentialRevision;
            return ValueTask.FromResult(Result);
        }

        private void Capture(string operation, uint accountId, ulong expectedStateRevision, AccountMutationContext context)
        {
            CallCount++;
            Operation = operation;
            AccountId = accountId;
            ExpectedStateRevision = expectedStateRevision;
            Context = context;
        }
    }
}
