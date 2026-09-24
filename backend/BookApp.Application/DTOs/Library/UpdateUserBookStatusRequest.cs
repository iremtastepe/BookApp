using BookApp.Domain.Enums;

namespace BookApp.Application.DTOs.Library;

public class UpdateUserBookStatusRequest
{
    public ReadingStatus Status { get; set; }
}