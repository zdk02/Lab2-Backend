using Lab2LinqApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi(); 
}

app.UseHttpsRedirection();

var authors = new List<Author>
{
    new() { AuthorId = 1, Name = "Ali",  BirthDate = new DateTime(1968,10,20), Country = "Lebanon" },
    new() { AuthorId = 2, Name = "Emma", BirthDate = new DateTime(1965, 3, 4), Country = "Egypt"   },
    new() { AuthorId = 3, Name = "Sami", BirthDate = new DateTime(1989, 5,25), Country = "Algeria" }
};

var books = new List<Book>
{
    new() { BookId = 1, Title = "Beauty and the Beast",   AuthorId = 1, Isbn = "bhggt",  PublishedYear = 2004 },
    new() { BookId = 2, Title = "The Fault in our Stars", AuthorId = 2, Isbn = "mnmwq",  PublishedYear = 2020 },
    new() { BookId = 3, Title = "Summer",                 AuthorId = 3, Isbn = "oqecve", PublishedYear = 2025 }
};

var borrowers = new List<Borrower>
{
    new() { BorrowerId = 1, Name = "Ali",    Email = "a@gmail.com", Phone = "81738099" },
    new() { BorrowerId = 2, Name = "Zeinab", Email = "z@gmail.com", Phone = "71345098" },
    new() { BorrowerId = 3, Name = "Mariam", Email = "m@gmail.com", Phone = "03564703" }
};

var loans = new List<Loan>
{
    new() { LoanId = 1, BookId = 1, BorrowerId = 1, LoanDate = new DateTime(2025,08,01), ReturnDate = new DateTime(2025,08,10), DueDate = new DateTime(2026,01,01), Returned = true  },
    new() { LoanId = 2, BookId = 2, BorrowerId = 2, LoanDate = new DateTime(2025,08,03), ReturnDate = null,                         DueDate = new DateTime(2025,08,14), Returned = false },
    new() { LoanId = 3, BookId = 3, BorrowerId = 3, LoanDate = new DateTime(2025,08,05), ReturnDate = new DateTime(2025,08,12),     DueDate = new DateTime(2026,07,13), Returned = true  }
};

int NextLoanId() => loans.Count == 0 ? 1 : loans.Max(l => l.LoanId) + 1;

static IOrderedEnumerable<T> OrderByDir<T, TKey>(IEnumerable<T> src, Func<T, TKey> key, string? order) =>
    string.Equals(order, "desc", StringComparison.OrdinalIgnoreCase) ? src.OrderByDescending(key) : src.OrderBy(key);

// 1) Books in a specific year, ordered asc/desc by release date
app.MapGet("/books/by-year", (int year, string? order) =>
{
    var q = OrderByDir(books.Where(b => b.PublishedYear == year),
                       b => b.PublishedYear, order)
            .ThenBy(b => b.Title);
    return Results.Ok(q);
});

// 2) Group authors born in the same year
app.MapGet("/authors/group-by-year", () =>
{
    var groups = authors
        .GroupBy(a => a.BirthDate.Year)
        .OrderBy(g => g.Key)
        .Select(g => new {
            Year = g.Key,
            Count = g.Count(),
            Authors = g.Select(a => new { a.AuthorId, a.Name, a.Country })
        });
    return Results.Ok(groups);
});

// 3) Group authors born in the same year AND same country
app.MapGet("/authors/group-by-year-country", () =>
{
    var groups = authors
        .GroupBy(a => new { Year = a.BirthDate.Year, a.Country })
        .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Country)
        .Select(g => new {
            g.Key.Year,
            g.Key.Country,
            Count = g.Count(),
            Authors = g.Select(a => new { a.AuthorId, a.Name })
        });
    return Results.Ok(groups);
});

// 4) Total number of books
app.MapGet("/books/count", () => new { total_books = books.Count });

// 5) Pagination over books
app.MapGet("/books/paged", (int pageSize = 10, int pageNumber = 1) =>
{
    if (pageSize <= 0 || pageNumber <= 0)
        return Results.BadRequest("pageSize and pageNumber must be >= 1");

    var skip = (pageNumber - 1) * pageSize;

    var items = books
        .OrderBy(b => b.PublishedYear)
        .ThenBy(b => b.Title)
        .Skip(skip)
        .Take(pageSize)
        .ToList();

    var total = books.Count;
    var totalPages = (int)Math.Ceiling(total / (double)pageSize);

    return Results.Ok(new {
        PageNumber = pageNumber,
        PageSize = pageSize,
        TotalItems = total,
        TotalPages = totalPages,
        Items = items
    });
});

// Overdue loans (returned = false and due_date < today)
app.MapGet("/loans/overdue", () =>
{
    var today = DateOnly.FromDateTime(DateTime.UtcNow).ToDateTime(new TimeOnly(0,0));
    var q = loans.Where(l => !l.Returned && l.DueDate < today);
    return Results.Ok(q);
});

// Join: Loans + Books + Borrowers for a borrower name
app.MapGet("/loans/by-borrower", (string name) =>
{
    var q =
        from l in loans
        join b in books on l.BookId equals b.BookId
        join br in borrowers on l.BorrowerId equals br.BorrowerId
        where br.Name == name
        select new { b.BookId, b.Title, l.LoanDate, l.ReturnDate, l.Returned };

    return Results.Ok(q);
});

// Popular books (BookId, Title, total_loans) like my view
app.MapGet("/books/popular", () =>
{
    var q =
        from b in books
        join l in loans on b.BookId equals l.BookId into bl
        orderby bl.Count() descending
        select new { b.BookId, b.Title, total_loans = bl.Count() };

    return Results.Ok(q);
});

// “borrow_book” procedure
app.MapPost("/loans/borrow", (int bookId, int borrowerId, DateOnly loanDate) =>
{
    if (!books.Any(b => b.BookId == bookId)) return Results.BadRequest("Invalid bookId.");
    if (!borrowers.Any(b => b.BorrowerId == borrowerId)) return Results.BadRequest("Invalid borrowerId.");

    var loan = new Loan
    {
        LoanId = NextLoanId(),
        BookId = bookId,
        BorrowerId = borrowerId,
        LoanDate = loanDate.ToDateTime(new TimeOnly(0,0)),
        DueDate = loanDate.ToDateTime(new TimeOnly(0,0)).AddDays(14),
        Returned = false
    };
    loans.Add(loan);
    return Results.Created($"/loans/{loan.LoanId}", loan);
});

// “return_book” procedure
app.MapPost("/loans/return", (int loanId, DateOnly returnDate) =>
{
    var loan = loans.FirstOrDefault(l => l.LoanId == loanId);
    if (loan is null) return Results.NotFound("Loan not found.");

    loan.Returned = true;
    loan.ReturnDate = returnDate.ToDateTime(new TimeOnly(0,0));
    return Results.Ok(loan);
});

app.Run();

record Borrower
{
    public int BorrowerId { get; set; }
    public string Name { get; set; } = default!;
    public string Email { get; set; } = default!;
    public string? Phone { get; set; }
}

record Loan
{
    public int LoanId { get; set; }
    public int BookId { get; set; }
    public int BorrowerId { get; set; }
    public DateTime LoanDate { get; set; }
    public DateTime? ReturnDate { get; set; }
    public DateTime DueDate { get; set; }
    public bool Returned { get; set; }
}