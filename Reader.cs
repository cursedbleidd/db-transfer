using System;
using System.Collections.Generic;

namespace db_transfer;

public partial class Reader
{
    public int ReaderId { get; set; }

    public string Name { get; set; } = null!;

    public virtual ICollection<Borrow> Borrows { get; set; } = new List<Borrow>();
}
