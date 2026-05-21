using Microsoft.AspNetCore.Identity;

var password = args.Length > 0 ? args[0] : "Password1!";
var hasher = new PasswordHasher<SeedUser>();
Console.WriteLine(hasher.HashPassword(new SeedUser(), password));

file sealed class SeedUser;
