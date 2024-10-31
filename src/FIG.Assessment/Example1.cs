using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;

namespace FIG.Assessment;

/// <summary>
/// In this example, the goal of this GetPeopleInfo method is to fetch a list of Person IDs from our database (not implemented / not part of this example),
/// and for each Person ID we need to hit some external API for information about the person. Then we would like to return a dictionary of the results to the caller.
/// We want to perform this work in parallel to speed it up, but we don't want to hit the API too frequently, so we are trying to limit to 5 requests at a time at most
/// by using 5 background worker threads.
/// Feel free to suggest changes to any part of this example.
/// In addition to finding issues and/or ways of improving this method, what is a name for this sort of queueing pattern? 
///     //RBG Note:I think this would be a thread pool pattern since you arr creating a pool of 5 threads to process everything in the queue. As a thread completes processing for
///     //one item in the queue it looks for the next item to process until the queue is empty.
/// </summary>
/// 

//RBG Overall questions/comments
    //In a real life example I would have additional questions concerning the response time of the API and if there was other complex logic we wanted to implement with the API results.
    //That said, the idea that we want to speed up processing (multiple threads) while also adding lag time between API calls is a bit strange. For this exact scenartio I don't see the 
    //benefit of multiple threads because the real limitation is the speed at which you hit the API. If this were a real use case, I would suggest the code be executed on a single thread
    //and async/await to get the results from the API. If the number of API calls per second still needed to be reduced we could add a delay that wouldn't block the main thread.
public class Example1
{
    //RBG Note: Create a single HTTP client since it is thread safe and prevents the creation of a new client for each 
    private static HttpClient? client;
    //RBG Note: I prefer to make this a constant and then use in the sleep method
    const int delay = 50; //Delay in miliseconds
    //RBG Note: I would make this static since I don't see a need for an instance of this object.
    public static Dictionary<int, int> GetPeopleInfo()
    {
        //RBG Note: setup the HTTP client using httpClientfactory. Perhaps this is outside the scope of this assessment but thought it was worth mentioning.
        var serviceCollection = new ServiceCollection();
        ConfigureServices(serviceCollection);
        var services = serviceCollection.BuildServiceProvider();
        var httpClientFactory = services.GetRequiredService<IHttpClientFactory>();
        client = httpClientFactory.CreateClient();

        // initialize empty queue, and empty result set
        //RBG Note: This should be a concurrent queue/dictionary since they are thread safe and we plan to use multiple threads
        var personIdQueue = new ConcurrentQueue<int>();
        var results = new ConcurrentDictionary<int, int>();

        // start thread which will populate the queue
        //RBG Note: We can get this data using the main thread since it is needed in order to create the other threads that will be used to call the API
        //RBG Note: No longer need this code -> var collectThread = new Thread(() => CollectPersonIds(personIdQueue));
        //RBG Note: No longer need this code -> collectThread.Start();
        CollectPersonIds(personIdQueue);

        // start 5 worker threads to read through the queue and fetch info on each item, adding to the result set
        var gatherThreads = new List<Thread>();
        for (var i = 0; i < 5; i++)
        {
            var gatherThread = new Thread(() => GatherInfo(personIdQueue, results));
            //RBG Note: I like to name the threads in order to use for logs and debugging
            gatherThread.Name = "Thread " + i;
            gatherThread.Start();
            gatherThreads.Add(gatherThread);
        }

        // wait for all threads to finish
        //RBG Note: No longer need this code -> collectThread.Join();
        foreach (var gatherThread in gatherThreads)
            gatherThread.Join();

        return new Dictionary<int, int>(results);
    }

    private static void CollectPersonIds(ConcurrentQueue<int> personIdQueue)
    {
        // dummy implementation, would be pulling from a database
        for (var i = 1; i < 100; i++)
        {
            //RBG Note: Since this is the collection of data to populate the queue and we are likely getting it from our DB we don't want to add a delay here. This method should return the PersonIds as quickly as possible.
            //RBG Note: No longer need this code -> if (i % 10 == 0) Thread.Sleep(TimeSpan.FromMilliseconds(50)); // artificial delay every now and then
            personIdQueue.Enqueue(i);
        }
    }

    private static void GatherInfo(ConcurrentQueue<int> personIdQueue, ConcurrentDictionary<int, int> results)
    {
        // pull IDs off the queue until it is empty
        while (personIdQueue.TryDequeue(out var id))
        {
            //RBG Logging: Console.WriteLine(Thread.CurrentThread.Name + " is processing " + id);
            //RBG Note: No longer need this code -> var client = new HttpClient();
            //RBG Debugging: var request = new HttpRequestMessage(HttpMethod.Get, $"https://google.com");
            var request = new HttpRequestMessage(HttpMethod.Get, $"https://some.example.api/people/{id}/age");
            //RBG Note: Wrap this in a try catch so we can process/handle any exceptions
            try
            {
                var response = client!.SendAsync(request).Result;
                //RBG Debugging: var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
                var age = int.Parse(response.Content.ReadAsStringAsync().Result);
                //RBG Debugging: int age = 10;
                //RBG Note: need to use TryAdd since it is a concurrent dictionary
                results.TryAdd(id, age);
                //RBG Note: No longer need this code -> results[id] = age;
                //RBG Note: Move sleep in to this function since the delay needs to happen relative to the API calls. 
                Thread.Sleep(TimeSpan.FromMilliseconds(delay));
            }
            catch (Exception ex)
            {
                //Do something with the exception like logging
            }
        }
    }
    //RBG Note: Configure the AddHttpClient service in order to use httpClientFactory
    private static void ConfigureServices(ServiceCollection services)
    {
        services.AddHttpClient();
    }
}
