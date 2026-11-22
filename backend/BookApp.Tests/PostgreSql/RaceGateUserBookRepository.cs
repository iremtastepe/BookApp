
using BookApp.Application.DTOs.Library;
using BookApp.Application.Interfaces;
using BookApp.Domain.Entities;

namespace BookApp.Tests.PostgreSql;

// İsteklerin ön kontrolü tamamlamasını bekleyen yarış kapısı.
public sealed class RaceGate
{
    private readonly int _participants;
    private readonly TaskCompletionSource _allArrived =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _arrived;

    public RaceGate(int participants)
    {
        if (participants < 2)
            throw new ArgumentOutOfRangeException(
                nameof(participants),
                "Yarış için en az 2 katılımcı gerekir.");

        _participants = participants;
    }

    public int Arrived => Volatile.Read(ref _arrived);

    public bool AllArrived =>
        _allArrived.Task.IsCompletedSuccessfully;

    public async Task ArriveAndWaitAsync(TimeSpan timeout)
    {
        if (Interlocked.Increment(ref _arrived) == _participants)
            _allArrived.TrySetResult();

        var finished = await Task.WhenAny(
            _allArrived.Task,
            Task.Delay(timeout));

        if (finished != _allArrived.Task)
        {
            throw new TimeoutException(
                $"Yarış kapısı zaman aşımı: {_participants} istekten " +
                $"yalnızca {Arrived} tanesi ön kontrolü geçip kapıya ulaştı.");
        }
    }
}

// Gerçek repository'yi sararak sadece ExistsAsync
// kontrolünden sonra bekleme uygular.
public sealed class RaceGateUserBookRepository : IUserBookRepository
{
    private readonly IUserBookRepository _inner;
    private readonly RaceGate _gate;
    private readonly TimeSpan _timeout;

    public RaceGateUserBookRepository(
        IUserBookRepository inner,
        RaceGate gate,
        TimeSpan timeout)
    {
        _inner = inner;
        _gate = gate;
        _timeout = timeout;
    }

    public async Task<bool> ExistsAsync(int userId, int bookId)
    {
        var exists = await _inner.ExistsAsync(userId, bookId);

        if (!exists)
            await _gate.ArriveAndWaitAsync(_timeout);

        return exists;
    }

    public Task<UserBook> AddAsync(UserBook userBook)
        => _inner.AddAsync(userBook);

    public Task<UserBook?> GetByIdWithBookAsync(int id, int userId)
        => _inner.GetByIdWithBookAsync(id, userId);

    public Task<List<UserBook>> GetAllByUserIdAsync(int userId)
        => _inner.GetAllByUserIdAsync(userId);

    public Task<(List<UserBook> Items, int TotalCount)> GetPagedByUserIdAsync(
        int userId, LibraryQuery query)
        => _inner.GetPagedByUserIdAsync(userId, query);

    public Task UpdateAsync(UserBook userBook)
        => _inner.UpdateAsync(userBook);

    public Task<int> GetReadBooksCountByYearAsync(int userId, int year)
        => _inner.GetReadBooksCountByYearAsync(userId, year);

    public Task<int> GetTotalReadBooksCountAsync(int userId)
        => _inner.GetTotalReadBooksCountAsync(userId);

    public Task<(List<UserBook> Items, int TotalCount)> GetMonthlyReadBooksPagedAsync(
        int userId, int year, int month, int page, int pageSize)
        => _inner.GetMonthlyReadBooksPagedAsync(
            userId, year, month, page, pageSize);
}
