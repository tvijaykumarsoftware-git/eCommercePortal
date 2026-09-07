using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using BCrypt.Net;
using EcommerceAPI.Data;
using EcommerceAPI.DTOs;
using EcommerceAPI.Models;
using Microsoft.AspNetCore.Authorization;

namespace EcommerceAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly EcommerceStoreDbContext _context;
        private readonly IConfiguration _configuration;

        public AuthController(EcommerceStoreDbContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromForm] RegisterDto dto)
        {
            // 1. Check if user already exists
            if (await _context.Users.AnyAsync(u => u.Email == dto.Email))
            {
                return BadRequest(new { message = "Email is already registered." });
            }

            if (dto.Image is not null && !IsSupportedImage(dto.Image))
            {
                return BadRequest(new { message = "Please upload a JPG, PNG, WEBP, or GIF image up to 5 MB." });
            }

            // 2. Hash password securely using BCrypt
            string passwordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password);

            // 3. Create user entity
            var user = new User
            {
                FirstName = dto.FirstName,
                LastName = dto.LastName,
                Email = dto.Email,
                ImageUrl = dto.Image is null ? null : await SaveImageAsync(dto.Image),
                Location = dto.Location,
                PhoneNumber = dto.PhoneNumber,
                PasswordHash = passwordHash,
                RoleId = dto.RoleId,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            return Ok(new { message = "User registered successfully." });
        }

        [Authorize]
        [HttpGet("profile")]
        public async Task<ActionResult<object>> GetProfile()
        {
            var user = await GetCurrentUserAsync();
            if (user is null) return Unauthorized();

            return Ok(new
            {
                user.UserId,
                user.FirstName,
                user.LastName,
                user.Email,
                user.ImageUrl,
                user.Location,
                user.PhoneNumber
            });
        }

        [Authorize]
        [HttpPut("profile")]
        public async Task<ActionResult<object>> UpdateProfile([FromForm] UpdateProfileDto dto)
        {
            var user = await GetCurrentUserAsync();
            if (user is null) return Unauthorized();
            if (dto.Image is not null && !IsSupportedImage(dto.Image))
            {
                return BadRequest(new { message = "Please upload a JPG, PNG, WEBP, or GIF image up to 5 MB." });
            }

            user.FirstName = dto.FirstName.Trim();
            user.LastName = dto.LastName.Trim();
            user.Location = string.IsNullOrWhiteSpace(dto.Location) ? null : dto.Location.Trim();
            user.PhoneNumber = string.IsNullOrWhiteSpace(dto.PhoneNumber) ? null : dto.PhoneNumber.Trim();
            if (dto.Image is not null) user.ImageUrl = await SaveImageAsync(dto.Image);

            await _context.SaveChangesAsync();
            return Ok(new
            {
                user.UserId,
                user.FirstName,
                user.LastName,
                user.Email,
                user.ImageUrl,
                user.Location,
                user.PhoneNumber,
                UserName = $"{user.FirstName} {user.LastName}"
            });
        }

        [HttpPost("login")]
        public async Task<ActionResult<AuthResponseDto>> Login([FromBody] LoginDto dto)
        {
            // 1. Fetch user including Role details
            var user = await _context.Users
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.Email == dto.Email);

            if (user == null || !user.IsActive)
            {
                return Unauthorized(new { message = "Invalid email or account is inactive." });
            }

            // 2. Verify password hash
            bool isPasswordValid = BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash);
            if (!isPasswordValid)
            {
                return Unauthorized(new { message = "Invalid credentials." });
            }

            // 3. Generate JWT Token
            var tokenString = GenerateJwtToken(user, out DateTime expiration);

            return Ok(new AuthResponseDto
            {
                Token = tokenString,
                UserName = $"{user.FirstName} {user.LastName}",
                Email = user.Email,
                ImageUrl = user.ImageUrl,
                Role = user.Role.RoleName,
                Expiration = expiration
            });
        }

        private string GenerateJwtToken(User user, out DateTime expiration)
        {
            var jwtSettings = _configuration.GetSection("JwtSettings");
            var key = Encoding.UTF8.GetBytes(jwtSettings["SecretKey"]!);

            expiration = DateTime.UtcNow.AddMinutes(double.Parse(jwtSettings["ExpiryInMinutes"] ?? "60"));

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Name, $"{user.FirstName} {user.LastName}"),
                new Claim(ClaimTypes.Role, user.Role.RoleName)
            };

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = expiration,
                Issuer = jwtSettings["Issuer"],
                Audience = jwtSettings["Audience"],
                SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(key),
                    SecurityAlgorithms.HmacSha256Signature
                )
            };

            var tokenHandler = new JwtSecurityTokenHandler();
            var token = tokenHandler.CreateToken(tokenDescriptor);

            return tokenHandler.WriteToken(token);
        }

        private async Task<User?> GetCurrentUserAsync()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(userId, out var parsedUserId)
                ? await _context.Users.FindAsync(parsedUserId)
                : null;
        }

        private static bool IsSupportedImage(IFormFile image)
        {
            var extension = Path.GetExtension(image.FileName).ToLowerInvariant();
            return image.Length > 0 && image.Length <= 5 * 1024 * 1024 && new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif" }.Contains(extension);
        }

        private async Task<string> SaveImageAsync(IFormFile image)
        {
            var uploadDirectory = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "profiles");
            Directory.CreateDirectory(uploadDirectory);
            var extension = Path.GetExtension(image.FileName).ToLowerInvariant();
            var fileName = $"{Guid.NewGuid():N}{extension}";
            var filePath = Path.Combine(uploadDirectory, fileName);
            await using var stream = System.IO.File.Create(filePath);
            await image.CopyToAsync(stream);
            return $"/uploads/profiles/{fileName}";
        }
    }
}