using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;


//RBG Overall questions/comments
    //Depending on the use case for this login I think there needs to be some tracking of attempts and an expontential delay added to prevent hacking attempts.
    //In addition, the account for a given user could be locked for some period of time if a total number of attempts was exceeded.

namespace FIG.Assessment;

public class Example2 : Controller
{
    public IConfiguration _config;

    public Example2(IConfiguration config)
        => this._config = config;

    //RBG Note: Assign a redirect for invalid Username and/or Password
    const string invalidUsernameOrPassRedirect = "/Error?msg=invalid_username_or_password";

    [HttpPost("login")]
    public async Task<IActionResult> LoginAsync([FromForm] LoginPostModel model)
    {
        var serviceCollection = new ServiceCollection();
        var services = serviceCollection.BuildServiceProvider();

        //RBG Note: Create the connection in a using statement so it will be properly disposed when the connection is no longer needed
        using (SqlConnection conn = new SqlConnection(this._config.GetConnectionString("SQL")))
        {
            //RBG Note: No longer need this code -> using var conn = new SqlConnection(this._config.GetConnectionString("SQL"));

            //RBG Note: Put the call in a try catch block so we can catch exceptions
            try
            {
                //should put the connection in a using statement
                await conn.OpenAsync();

                //RBG Note: I personally would create this as a stored procedure and call it passing the username to the procedure. This allows us to edit/alter the logic in the query without a code change.
                    //Also, the username should be sent as a parameter to prevent sql injection
                //RBG Note: No longer need this code -> var sql = $"SELECT u.UserID, u.PasswordHash FROM User u WHERE u.UserName='{model.UserName}';";
                var sql = $"SELECT u.UserID, u.PasswordHash FROM User u WHERE u.UserName=@userName;";
                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@userName", model.UserName);
                using var reader = await cmd.ExecuteReaderAsync();

                // first check user exists by the given username
                if (!reader.Read())
                {
                    //RBG Note: Typically not a good idea to indicate that the username is invalid. It is better to say the username/password combo is invalid
                        //Could add logging or update a DB table to track invalid logins
                    //RBG Note: No longer need this code -> return this.Redirect("/Error?msg=invalid_username");
                    return this.Redirect(invalidUsernameOrPassRedirect);
                }
                // then check password is correct
                //RBG Notes: likely want to update the hashing algorithm for this since MD5 is not considered good for password hashing. This would also need to be updated in the database
                    //and consider including password salt since it is a best practice for hashing passwords.
                var inputPasswordHash = MD5.HashData(Encoding.UTF8.GetBytes(model.Password));
                var databasePasswordHash = (byte[])reader["PasswordHash"];

                if (!databasePasswordHash.SequenceEqual(inputPasswordHash))
                {
                    //RBG Note: Returning that the password is invalid indicates that the username is valid. This can be a security issue so it is better to say the username/password combo is invalid
                        //Could add logging or update a DB table to track invalid logins
                    //RBG Note: No longer need this code -> return this.Redirect("/Error?msg=invalid_password");
                    return this.Redirect(invalidUsernameOrPassRedirect);
                }
                // if we get this far, we have a real user. sign them in
                var userId = (int)reader["UserID"];
                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                };
                var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                var principal = new ClaimsPrincipal(identity);
                await this.HttpContext.SignInAsync(principal);

                return this.Redirect(model.ReturnUrl);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                //RBG Note: Depending on the exception thrown the redirect might change
                return this.Redirect("CouldNotLogin");
            }
        }
    }
}

public class LoginPostModel
{
    public string UserName { get; set; }

    public string Password { get; set; }

    public string ReturnUrl { get; set; }
}

