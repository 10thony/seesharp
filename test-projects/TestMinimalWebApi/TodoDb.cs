using Microsoft.EntityFrameworkCore;
using TestMinimalWebApi.Models;

namespace TestMinimalWebApi;

public class TodoDb : DbContext
{
    public TodoDb(DbContextOptions<TodoDb> options)
        : base(options)
    {
    }

    public DbSet<Todo> Todos => Set<Todo>();
}
