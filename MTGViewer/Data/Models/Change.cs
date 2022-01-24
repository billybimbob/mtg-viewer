using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace MTGViewer.Data;

[Index(
    nameof(TransactionId),
    nameof(FromId),
    nameof(ToId),
    nameof(CardId), IsUnique = true)]
public class Change
{
    [JsonIgnore]
    public int Id { get; private set; }


    [JsonIgnore]
    public string CardId { get; init; } = null!;
    public Card Card { get; init; } = null!;


    [Range(1, int.MaxValue)]
    public int Amount { get; set; }


    [JsonIgnore]
    public int ToId { get; init; }
    public Location To { get; init; } = null!;


    [JsonIgnore]
    public int? FromId { get; init; }
    public Location? From { get; init; } = null!;


    [JsonIgnore]
    public int TransactionId { get; init; }
    public Transaction Transaction { get; init; } = null!;
}


public class Transaction
{
    [JsonIgnore]
    public int Id { get; private set; }

    [Display(Name = "Applied At")]
    public DateTime AppliedAt { get; private set; }

    public List<Change> Changes { get; init; } = new();
}