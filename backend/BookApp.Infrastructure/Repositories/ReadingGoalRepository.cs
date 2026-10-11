using BookApp.Application.Interfaces;
using BookApp.Domain.Entities;
using BookApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BookApp.Infrastructure.Repositories;

public class ReadingGoalRepository : IReadingGoalRepository
{
    private readonly AppDbContext _context;

    public ReadingGoalRepository(AppDbContext context)
    {
        _context = context;
    }

    public Task<ReadingGoal?> GetByUserAndYearAsync(int userId, int year)
        => _context.ReadingGoals
            .FirstOrDefaultAsync(rg => rg.UserId == userId && rg.Year == year);

    public async Task<ReadingGoal> AddAsync(ReadingGoal goal)
    {
        _context.ReadingGoals.Add(goal);
        await _context.SaveChangesAsync();
        return goal;
    }

    public Task UpdateAsync(ReadingGoal goal)
    {
        _context.ReadingGoals.Update(goal);
        return _context.SaveChangesAsync();
    }
}
