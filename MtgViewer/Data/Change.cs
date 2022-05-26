using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

using Microsoft.EntityFrameworkCore;

namespace MtgViewer.Data;

[Index(nameof(CardId), nameof(ToId), nameof(FromId), nameof(TransactionId), IsUnique = true)]
[Index(nameof(Copies), nameof(Id), nameof(CardId), nameof(ToId), nameof(FromId), nameof(TransactionId))]
public class Change
{
    [JsonIgnore]
    public int Id { get; private set; }

    [JsonIgnore]
    public Card Card { get; init; } = default!;
    public string CardId { get; init; } = default!;

    [Range(1, 4_096)]
    public int Copies { get; set; }

    [JsonIgnore]
    public int ToId { get; init; }
    public Location To { get; init; } = default!;

    [JsonIgnore]
    public int? FromId { get; init; }
    public Location? From { get; init; } = default!;

    [JsonIgnore]
    public int TransactionId { get; init; }
    public Transaction Transaction { get; init; } = default!;
}

[Index(nameof(AppliedAt), IsUnique = true)]
public class Transaction
{
    [JsonIgnore]
    public int Id { get; private set; }

    [Display(Name = "Applied At")]
    public DateTime AppliedAt { get; private set; }

    public List<Change> Changes { get; init; } = new();
}
