using BookApp.Domain.Entities;

namespace BookApp.Application.Interfaces;

public interface IReadingGoalRepository
{
    Task<ReadingGoal?> GetByUserAndYearAsync(int userId, int year);
    Task<ReadingGoal> AddAsync(ReadingGoal goal);
    Task UpdateAsync(ReadingGoal goal);
}
