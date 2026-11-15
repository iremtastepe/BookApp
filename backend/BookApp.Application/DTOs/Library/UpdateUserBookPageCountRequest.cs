
using System.Text.Json.Serialization;

namespace BookApp.Application.DTOs.Library;

public class UpdateUserBookPageCountRequest
{
    // JSON içinde pageCount alanı mutlaka bulunmalı.
    // Açıkça null gönderilmesi hâlâ kabul edilir.
    [JsonRequired]
    public int? PageCount { get; set; }
}
