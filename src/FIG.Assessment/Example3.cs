using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FIG.Assessment;

/// <summary>
/// In this example, we are writing a service that will run (potentially as a windows service or elsewhere) and once a day will run a report on all new
/// users who were created in our system within the last 24 hours, as well as all users who deactivated their account in the last 24 hours. We will then
/// email this report to the executives so they can monitor how our user base is growing.
/// </summary>
/// 
//RBG Overall questions/comments
    //Depending on where this needs to run I think it would be better to create it as a scheduled task rather than a windows service. Another option is to run it as a SQL job.
    //I went ahead with the example as it is currently but this doesn't seem like the best approach for the problem at hand. A better solution might be to populate another table
    //with the activated and deactivated users as they are activated/deactivated. That should solve the issue of any complex logic or long running queries (perhaps you only want
    //users that meet a specific criteria) since it would be done before the request for data. Entries in this table could be deleted daily in this case to prevent the table from
    //growing too large.
public class Example3
{
    public static void Main(string[] args)
    {
        Host.CreateDefaultBuilder(args)
            .ConfigureServices(services =>
            {
                services.AddDbContext<MyContext>(options =>
                {
                    options.UseSqlServer("dummy-connection-string");
                });
                services.AddSingleton<ReportEngine>();
                services.AddHostedService<DailyReportService>();
            })
            .Build()
            .Run();
    }
}

public class DailyReportService : BackgroundService
{
    //RBG Note: added a logger so we can log the service starting, processing, and ending
    private readonly ILogger<DailyReportService> _logger;
    private readonly ReportEngine _reportEngine;

    public DailyReportService(ReportEngine reportEngine, ILogger<DailyReportService> logger)
    {
        _reportEngine = reportEngine;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        //RBG Note: Best practice is to add logging here to indicate that the application started.
        _logger.LogInformation("DailyReportService started at: " + DateTime.UtcNow);

        //RBG Note: We need to make sure all times are in the same time zone in order to be sure we are generating the correct data.
            //It is important to keep in mind that 'startingFrom' depends on the time when the service was started. So if it was stopped 
            //and restarted at noon then it will send an email with users from noon the day before up until noon today.
            //Also, every time the service restarts it will send another email and that might be confusing for end users. A better solution 
            //might be to keep a log of the last run timestamp and use that as the starting time.
        //RBG Note: Adding an ending time to this logic so we know starting time is exactly 24 hours ago. In most cases it won't make a difference
            //but the delay could cause some records to be missed.
        var endingAt = DateTime.Now;
        //when the service starts up, start by looking back at the last 24 hours
        var startingFrom = endingAt.AddDays(-1);

        while (!stoppingToken.IsCancellationRequested)
        {
            //Put this in a try catch block so exceptions can be handled.
            try
            {
                var newUsersTask = this._reportEngine.GetNewUsersAsync(startingFrom, endingAt);
                var deactivatedUsersTask = this._reportEngine.GetDeactivatedUsersAsync(startingFrom, endingAt);
                await Task.WhenAll(newUsersTask, deactivatedUsersTask); // run both queries in parallel to save time

                //RBG Note: This could be handled in the 'SendUserReportAsync' method but it's good to think about when the email is being delivered to 
                    //the user. In a lot of cases it is a good idea to perform the processing at non-peak times, if those exist for the system, and deliver
                    //notifications during normal business hours
                // send report to execs
                await this.SendUserReportAsync(newUsersTask.Result, deactivatedUsersTask.Result);

                //RBG Note: Set starting time to ending time so there is no gap in the time between requests.
                // save the current time, wait 24hr, and run the report again - using the new cutoff date
                startingFrom = endingAt;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, ex.Message);
            }
            //RBG Note: Added a 'finally' so the delay occurs after success or an exception. Depending on the requirements this might need to be updated.
            finally
            {
                await Task.Delay(TimeSpan.FromHours(24));
            }

        }
        //RBG Note: Best practice is to add logging here to indicate that the application stopped.
        _logger.LogInformation("DailyReportService stopped at: " + DateTime.UtcNow);
        return;
    }

    private Task SendUserReportAsync(IEnumerable<User> newUsers, IEnumerable<User> deactivatedUsers)
    {
        // not part of this example
        return Task.CompletedTask;
    }
}

/// <summary>
/// A dummy report engine that runs queries and returns results.
/// The queries here a simple but imagine they might be complex SQL queries that could take a long time to complete.
/// </summary>
public class ReportEngine
{
    private readonly MyContext _db;

    public ReportEngine(MyContext db) => _db = db;

    //RBG Note: Added an ending time for the query
    public async Task<IEnumerable<User>> GetNewUsersAsync(DateTime startingFrom, DateTime endingAt)
    {
        var newUsers = (await this._db.Users.ToListAsync())
            .Where(u => u.CreatedAt > startingFrom && u.CreatedAt <= endingAt);
        return newUsers;
    }

    //RBG Note: Added an ending time for the query
    public async Task<IEnumerable<User>> GetDeactivatedUsersAsync(DateTime startingFrom, DateTime endingAt)
    {
        var deactivatedUsers = (await this._db.Users.ToListAsync())
            //RBG Note: Added a null value check to the query
            .Where(u => u.DeactivatedAt != null && u.DeactivatedAt > startingFrom && u.DeactivatedAt <= endingAt);
        return deactivatedUsers;
    }
}

#region Database Entities
// a dummy EFCore dbcontext - not concerned with actually setting up connection strings or configuring the context in this example
public class MyContext : DbContext
{
    public MyContext(DbContextOptions<MyContext> options) : base(options)
    { }
    public DbSet<User> Users { get; set; }
}

public class User
{
    public int UserId { get; set; }

    public string UserName { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? DeactivatedAt { get; set; }
}
#endregion