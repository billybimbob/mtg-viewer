using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace MTGViewer.Data;


[Index(nameof(TransactionId), nameof(FromId), nameof(ToId), nameof(CardId), IsUnique = true)]
[Index(nameof(Copies), nameof(Id), nameof(TransactionId), nameof(FromId), nameof(ToId), nameof(CardId))]
public class Change
{
    [Key]
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
[Index(nameof(AppliedAt), nameof(Id))]
public class Transaction
{
    [JsonIgnore]
    public int Id { get; private set; }

    [Display(Name = "Applied At")]
    public DateTime AppliedAt { get; private set; }

    public List<Change> Changes { get; init; } = new();
}
