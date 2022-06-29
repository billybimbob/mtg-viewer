using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

using Microsoft.EntityFrameworkCore;

namespace MtgViewer.Data;

[Index(nameof(AppliedAt), IsUnique = true)]
public class Transaction
{
    [JsonIgnore]
    public int Id { get; private set; }

    [Display(Name = "Applied At")]
    public DateTime AppliedAt { get; private set; }

    public List<Change> Changes { get; init; } = new();
}
