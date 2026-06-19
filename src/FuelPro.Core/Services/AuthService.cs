using FuelPro.Core.Common;
using FuelPro.Core.Models;
using FuelPro.Core.Repositories;
using Serilog;

namespace FuelPro.Core.Services;

/// <summary>
/// Authentication service for PIN-based login.
/// </summary>
public class AuthService
{
    private readonly IUserRepository _userRepo;
    private readonly ILogger _logger = Log.ForContext<AuthService>();

    public User? CurrentUser { get; private set; }

    /// <summary>
    /// True if the currently logged-in user is a Manager (or legacy Admin).
    /// </summary>
    public bool IsManager => CurrentUser?.IsManager ?? false;

    /// <summary>
    /// True if the currently logged-in user is an Owner.
    /// </summary>
    public bool IsOwner => CurrentUser?.IsOwner ?? false;

    /// <summary>
    /// True if the currently logged-in user is a Developer.
    /// </summary>
    public bool IsDeveloper => CurrentUser?.IsDeveloper ?? false;

    public AuthService(IUserRepository userRepo) => _userRepo = userRepo;

    public async Task<Result<User>> LoginAsync(string username, string pin)
    {
        try
        {
            var result = await _userRepo.GetByUsernameAsync(username);
            if (!result.Success || result.Data == null)
                return Result<User>.Fail("Invalid username or PIN");

            var user = result.Data;
            if (!BCrypt.Net.BCrypt.Verify(pin, user.PinHash))
                return Result<User>.Fail("Invalid username or PIN");

            CurrentUser = user;
            _logger.Information("User {Username} logged in successfully", username);
            return Result<User>.Ok(user);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Login failed for {Username}", username);
            return Result<User>.Fail($"Login error: {ex.Message}");
        }
    }

    public async Task<Result> ChangePinAsync(int userId, string currentPin, string newPin)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(newPin) || newPin.Length < 4)
                return Result.Fail("PIN must be at least 4 digits");

            var allUsersResult = await _userRepo.GetAllUsersAsync();
            var user = allUsersResult.Data?.FirstOrDefault(u => u.UserId == userId);
            if (user == null) return Result.Fail("User not found");

            // Prevent changing Developer PIN via application UI
            if (string.Equals(user.Role, "Developer", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(user.Username, "Developer", StringComparison.OrdinalIgnoreCase))
            {
                return Result.Fail("Developer PIN cannot be modified.");
            }

            if (!BCrypt.Net.BCrypt.Verify(currentPin, user.PinHash))
                return Result.Fail("Current PIN is incorrect");

            user.PinHash = BCrypt.Net.BCrypt.HashPassword(newPin);
            user.MustChangePin = false;
            return await _userRepo.UpdateUserAsync(user);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to change PIN for user {UserId}", userId);
            return Result.Fail($"Failed to change PIN: {ex.Message}");
        }
    }

    public async Task<Result<User>> CreateUserAsync(string username, string pin, string role)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(username))
                return Result<User>.Fail("Username is required");
            if (string.IsNullOrWhiteSpace(pin) || pin.Length < 4)
                return Result<User>.Fail("PIN must be at least 4 digits");

            var user = new User
            {
                Username = username,
                PinHash = BCrypt.Net.BCrypt.HashPassword(pin),
                Role = role,
                IsActive = true,
                MustChangePin = true,
                CreatedAt = DateTime.Now
            };

            return await _userRepo.CreateUserAsync(user);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to create user {Username}", username);
            return Result<User>.Fail($"Failed to create user: {ex.Message}");
        }
    }

    public void Logout()
    {
        _logger.Information("User {Username} logged out", CurrentUser?.Username);
        CurrentUser = null;
    }
}
