using System;
using System.Collections.Generic;

namespace db_transfer;

public partial class Library
{
    public int Id { get; set; }

    public string BookTitle { get; set; } = null!;

    public string BookGenre { get; set; } = null!;

    public string AuthorName { get; set; } = null!;

    public string ReaderName { get; set; } = null!;

    public DateOnly BorrowDate { get; set; }

    public DateOnly? ReturnDate { get; set; }

    public override string ToString() =>
        $"{BookTitle} {AuthorName} {ReaderName} {BorrowDate}";
}
