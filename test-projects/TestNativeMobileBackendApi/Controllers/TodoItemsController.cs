using Microsoft.AspNetCore.Mvc;
using TestNativeMobileBackendApi.Interfaces;
using TestNativeMobileBackendApi.Models;

namespace TestNativeMobileBackendApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TodoItemsController : ControllerBase
{
    private readonly ITodoRepository _todoRepository;

    public TodoItemsController(ITodoRepository todoRepository)
    {
        _todoRepository = todoRepository;
    }

    [HttpGet]
    public IActionResult List()
    {
        return Ok(_todoRepository.All);
    }

    [HttpPost]
    public IActionResult Create([FromBody] TodoItem item)
    {
        try
        {
            if (item == null || !ModelState.IsValid)
            {
                return BadRequest(ErrorCode.TodoItemNameAndNotesRequired.ToString());
            }

            if (_todoRepository.DoesItemExist(item.ID))
            {
                return StatusCode(StatusCodes.Status409Conflict, ErrorCode.TodoItemIDInUse.ToString());
            }

            _todoRepository.Insert(item);
        }
        catch
        {
            return BadRequest(ErrorCode.CouldNotCreateItem.ToString());
        }

        return Ok(item);
    }

    [HttpPut]
    public IActionResult Edit([FromBody] TodoItem item)
    {
        try
        {
            if (item == null || !ModelState.IsValid)
            {
                return BadRequest(ErrorCode.TodoItemNameAndNotesRequired.ToString());
            }

            var existingItem = _todoRepository.Find(item.ID);
            if (existingItem == null)
            {
                return NotFound(ErrorCode.RecordNotFound.ToString());
            }

            _todoRepository.Update(item);
        }
        catch
        {
            return BadRequest(ErrorCode.CouldNotUpdateItem.ToString());
        }

        return NoContent();
    }

    [HttpDelete("{id}")]
    public IActionResult Delete(string id)
    {
        try
        {
            var item = _todoRepository.Find(id);
            if (item == null)
            {
                return NotFound(ErrorCode.RecordNotFound.ToString());
            }

            _todoRepository.Delete(id);
        }
        catch
        {
            return BadRequest(ErrorCode.CouldNotDeleteItem.ToString());
        }

        return NoContent();
    }
}
