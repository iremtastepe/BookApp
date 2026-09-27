using BookApp.Application.DTOs.External;

namespace BookApp.Application.Interfaces.ExternalServices;

public interface IBookLookupService
{
    Task<ExternalBookDto?> SearchByIsbnAsync(string isbn);
}