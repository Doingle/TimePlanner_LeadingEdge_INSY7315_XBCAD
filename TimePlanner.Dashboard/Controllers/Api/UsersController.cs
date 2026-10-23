using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TimePlanner.Dashboard.Services.Users;

namespace TimePlanner.Dashboard.Controllers.Api
{
    //-----------------------------
    //account administration, for administrators only
    [Authorize(Roles = "Admin")]
    public class UsersController : ApiControllerBase
    {
        private readonly UserAdminService _admin;

        public UsersController(UserAdminService admin) => _admin = admin;

        public record CreateUserRequest(
            [Required, StringLength(UserAdminService.MaxNameLength)] string Name,
            [Required, EmailAddress, StringLength(256)] string Email,
            [Required] string Role);

        [HttpGet]
        [ProducesResponseType<List<UserSummary>>(StatusCodes.Status200OK)]
        public async Task<ActionResult<List<UserSummary>>> List() => await _admin.ListAsync();

        //-----------------------------
        //201 with the new person and their temporary password. The password is shown here once and cannot be fetched again, only replaced by a reset
        [HttpPost]
        [ProducesResponseType<PasswordResult>(StatusCodes.Status201Created)]
        [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<PasswordResult>> Create(CreateUserRequest request)
        {
            var result = await _admin.CreateAsync(request.Name, request.Email, request.Role);
            return result.Success
                ? Created($"/api/v1/users/{result.User!.Id}", result)
                : Problem(title: result.Error, statusCode: StatusCodes.Status400BadRequest);
        }

        [HttpPost("{id}/reset-password")]
        [ProducesResponseType<PasswordResult>(StatusCodes.Status200OK)]
        [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
        [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<PasswordResult>> ResetPassword(string id)
        {
            var result = await _admin.ResetPasswordAsync(id);
            return result.Success ? result : Failure(result.Error);
        }

        [HttpPost("{id}/deactivate")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
        [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Deactivate(string id)
        {
            var result = await _admin.SetActiveAsync(id, active: false);
            return result.Success ? NoContent() : Failure(result.Error);
        }

        [HttpPost("{id}/reactivate")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
        [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Reactivate(string id)
        {
            var result = await _admin.SetActiveAsync(id, active: true);
            return result.Success ? NoContent() : Failure(result.Error);
        }

        private ObjectResult Failure(string? error) =>
            Problem(title: error, statusCode: error == UserAdminService.NotFound ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest);
    }
}
//------------------------------EOF-----------------------------\\
