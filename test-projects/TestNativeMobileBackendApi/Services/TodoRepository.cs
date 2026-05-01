using TestNativeMobileBackendApi.Interfaces;
using TestNativeMobileBackendApi.Models;

namespace TestNativeMobileBackendApi.Services;

public class TodoRepository : ITodoRepository
{
    private readonly List<TodoItem> _todoList;

    public TodoRepository()
    {
        _todoList =
        [
            new TodoItem
            {
                ID = "6bb8a868-dba1-4f1a-93b7-24ebce87e243",
                Name = "Learn app development",
                Notes = "Take Microsoft Learn Courses",
                Done = true
            },
            new TodoItem
            {
                ID = "b94afb54-a1cb-4313-8af3-b7511551b33b",
                Name = "Develop apps",
                Notes = "Use Visual Studio and Visual Studio Code",
                Done = false
            },
            new TodoItem
            {
                ID = "ecfa6f80-3671-4911-aabe-63cc442c1ecf",
                Name = "Publish apps",
                Notes = "All app stores",
                Done = false
            }
        ];
    }

    public IEnumerable<TodoItem> All => _todoList;

    public bool DoesItemExist(string id) => _todoList.Any(item => item.ID == id);

    public TodoItem? Find(string id) => _todoList.FirstOrDefault(item => item.ID == id);

    public void Insert(TodoItem item) => _todoList.Add(item);

    public void Update(TodoItem item)
    {
        var existing = Find(item.ID);
        if (existing is null)
        {
            return;
        }

        var index = _todoList.IndexOf(existing);
        _todoList[index] = item;
    }

    public void Delete(string id)
    {
        var existing = Find(id);
        if (existing is not null)
        {
            _todoList.Remove(existing);
        }
    }
}
