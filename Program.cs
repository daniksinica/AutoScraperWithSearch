using HtmlAgilityPack;
using OpenAI.Chat;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading.Tasks;

public class Program
{
    public static async Task Main(string[] args)
    {
        InteractionWithUser ui = new InteractionWithUser();
        InteractionWithOpenAI openAi = new InteractionWithOpenAI("gpt-4o-mini", Environment.GetEnvironmentVariable("OPENAI_API_KEY"));
        Parser parser = new Parser();

        string userRequest = ui.UserInput();

        Console.WriteLine("\n[1/4] Analyzing your request and building filters...");
        string jsonResponse = await openAi.GetJSON(userRequest);

        JsonStructure filters = JsonSerializer.Deserialize<JsonStructure>(jsonResponse, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (filters == null || !filters.IsReadyForSearch)
        {
            Console.WriteLine("\nClarification needed: " + (filters?.ErrorMessage ?? "Failed to parse request."));
            return;
        }

        Console.WriteLine($"\n[2/4] Searching for {filters.Manufacturer} {filters.Model} on SS.lv...");
        string searchUrl = parser.UrlBuilder(filters);
        string htmlCatalog = await parser.GetHtmlForm(searchUrl, filters);
        List<string> carLinks = parser.ExtractLinks(htmlCatalog);

        Console.WriteLine($"Found {carLinks.Count} potential ads.");

        if (carLinks.Count == 0)
        {
            Console.WriteLine("No cars found matching your basic criteria.");
            return;
        }

        Console.WriteLine("\n[3/4] Starting deep text analysis...");

        int maxMatchesTarget = 5;
        int matchesFound = 0;

        List<ScrapedCarData> finalGoodCars = new List<ScrapedCarData>();
        List<CarAnalysisResult> finalAnalyses = new List<CarAnalysisResult>();

        foreach (string link in carLinks)
        {
            if (matchesFound >= maxMatchesTarget)
            {
                Console.WriteLine($"\nTarget reached: {maxMatchesTarget} matches found. Stopping search to save resources.");
                break;
            }

            Console.WriteLine($"Checking ad: {link}");

            string carHtml = await parser.GetSingleCarHtml(link);
            ScrapedCarData scrapedData = parser.ParseSingleCarPage(carHtml, link);

            CarAnalysisResult analysis = await openAi.AnalyzeCarMatchAsync(userRequest, scrapedData);

            if (analysis != null && analysis.MatchPercentage >= 50)
            {
                matchesFound++;
                finalGoodCars.Add(scrapedData);
                finalAnalyses.Add(analysis);
                Console.WriteLine($" ---> MATCH! Score: {analysis.MatchPercentage}%");
            }
        }

        Console.WriteLine("\n[4/4] Process completed!");
        ui.DisplayResults(finalGoodCars, finalAnalyses);
    }
}

public class ScrapedCarData
{
    public string Url { get; set; }
    public int Price { get; set; }
    public string Description { get; set; }
    public string MainParameters { get; set; }
    public string AdditionalFeatures { get; set; }
}

public class CarAnalysisResult
{
    public int MatchPercentage { get; set; }
    public string Reasoning { get; set; }
}

public class InteractionWithUser
{
    public string UserInput()
    {
        Console.WriteLine("Hello! Write your car criteria here: ");

        while (true)
        {
            string UserRequest = Console.ReadLine();

            if (string.IsNullOrEmpty(UserRequest) || UserRequest.Length < 3)
            {
                Console.WriteLine("Not enough criteria for search. Please, write more specific request!");
            }
            else
            {
                return UserRequest;
            }
        }
    }

    public void DisplayResults(List<ScrapedCarData> cars, List<CarAnalysisResult> analyses)
    {
        Console.WriteLine("\n================ FINAL RESULTS ================");

        if (cars.Count == 0)
        {
            Console.WriteLine("No cars matched your exact text requirements.");
            Console.WriteLine("===============================================");
            return;
        }

        for (int i = 0; i < cars.Count; i++)
        {
            Console.WriteLine($"Match Score : {analyses[i].MatchPercentage}%");
            Console.WriteLine($"URL         : {cars[i].Url}");
            Console.WriteLine($"Price       : {cars[i].Price} EUR");
            Console.WriteLine($"AI Reason   : {analyses[i].Reasoning}");
            Console.WriteLine("-----------------------------------------------");
        }
    }
}

public class InteractionWithOpenAI
{

    string InstructionForAI = "You are a car parameter extraction system. Your goal is to return strictly valid JSON in the specified format.\r\n\r\n\r\n\r\nRule 1: If the user's request does not contain at least two parameters from the list, set IsReadyForSearch to false and write a polite clarifying question in ErrorMessage.\r\n\r\nRule 2: All keys and values ​​in the JSON must be in English, even if the user is writing in Russian.\r\n\r\nRule 3: Take values ​​for bodies, boxes and colors only from the approved list string Bodytypes:[\"Cabriolet\", \"Coupe\", \"Hatchback\", \" Jeep\", \"Minibus\", \"Miniven\", \"Pickup\", \"Sedan\", \"Universal\", \"-\"]\r\n\r\nRule 4: If the user provides only a car model (e.g., 'X5' or 'A4'), automatically infer and add the corresponding Manufacturer (e.g., 'BMW' or 'Audi') to the Manufacturer array.\r\n\r\nRule 5: The user can search for ONLY ONE manufacturer per request. If they ask for multiple (e.g., BMW and Audi), set IsReadyForSearch to false and ask them to choose one.\r\n\r\nGearbox types: [\"Automatic\",\"Manual\"]\r\n\r\nColors: [\"Black\",\"Blue\",\"Brown\",\"Dark red\",\"Green\",\"Grey\",\"Ligh blue\",\"Orange\",\"Purple\",\"Red\",\"Silver\",\"White\",\"Yellow\",\"-\"].\r\nNo extra text outside of JSON.";
    string OpenaiApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
    string GptModel = "gpt-4o-mini";
    ChatClient CompleteRequest1;

    public InteractionWithOpenAI(string GptModel, string OpenaiApiKey)
    {
        CompleteRequest1 = new ChatClient(GptModel, OpenaiApiKey);
    }

    public async Task<string> GetJSON(string UserRequest)
    {
        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(InstructionForAI),
            new UserChatMessage(UserRequest)
        };

        var options = new ChatCompletionOptions
        {
            ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat(),
            Temperature = 0.1f
        };

        ChatCompletion response = await CompleteRequest1.CompleteChatAsync(messages, options);

        string JsonResponse = response.Content[0].Text;

        return JsonResponse;
    }

    public async Task<CarAnalysisResult> AnalyzeCarMatchAsync(string originalUserRequest, ScrapedCarData carData)
    {
        string analyzerInstruction = @"You are a car matching assistant. 
Compare the user's specific text request with the scraped car ad.
Return valid JSON. 

Format:
{
  ""MatchPercentage"": integer (0-100 based on how well the car meets the specific text requirements like 'leather', 'no rust', etc.),
  ""Reasoning"": ""Short 1-sentence explanation in English""
}";

        string adContext = $@"
SELLER DESCRIPTION: {carData.Description}
MAIN PARAMETERS: {carData.MainParameters}
ADDITIONAL FEATURES: {carData.AdditionalFeatures}";

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(analyzerInstruction),
            new UserChatMessage($"USER REQUEST: {originalUserRequest}\n\nCAR LISTING DATA:\n{adContext}")
        };

        var options = new ChatCompletionOptions
        {
            ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat(),
            Temperature = 0.2f
        };

        ChatCompletion response = await CompleteRequest1.CompleteChatAsync(messages, options);
        string jsonResponse = response.Content[0].Text;

        return JsonSerializer.Deserialize<CarAnalysisResult>(jsonResponse, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

}

public class JsonStructure
{
    public bool IsReadyForSearch { get; set; }
    public string ErrorMessage { get; set; }
    public string Manufacturer { get; set; }
    public string Model { get; set; }
    public string[] EngineType { get; set; }

    public string[] CarBodytype { get; set; }
    public string GearboxType { get; set; }
    public string[] Color { get; set; }
    public int? MinPrice { get; set; }
    public int? MaxPrice { get; set; }
    public int? MinYear { get; set; }
    public int? MaxYear { get; set; }
    public double? MinEngineVolume { get; set; }
    public double? MaxEngineVolume { get; set; }
}

public class Parser
{
    public string UrlBuilder(JsonStructure data)
    {
        string DefaultLink = $"https://www.ss.lv/en/transport/cars/";
        string name = data.Manufacturer?.ToLower().Replace(" ", "-") ?? "";
        string model;
        string SearchLink = DefaultLink + $"{name}";

        if (!string.IsNullOrEmpty(data.Model))
        {
            model = data.Model.ToLower().Replace(" ", "-");
            SearchLink += $"/{model}";
        }
        return SearchLink += "/filter/";
    }

    public async Task<string> GetSingleCarHtml(string carUrl)
    {
        HttpResponseMessage response = await client.GetAsync(carUrl);
        return await response.Content.ReadAsStringAsync();
    }

    private static readonly HttpClient client = new HttpClient();

    public async Task<string> GetHtmlForm(string SearchLink, JsonStructure data)
    {
        var formData = new Dictionary<string, string>();

        if (data.MinPrice != null)
        {
            formData.Add("topt[8][min]", data.MinPrice.Value.ToString());
        }
        if (data.MaxPrice != null)
        {
            formData.Add("topt[8][max]", data.MaxPrice.Value.ToString());
        }
        if (data.MinYear != null)
        {
            formData.Add("topt[18][min]", data.MinYear.Value.ToString());
        }
        if (data.MaxYear != null)
        {
            formData.Add("topt[18][max]", data.MaxYear.Value.ToString());
        }
        if (data.MinEngineVolume != null)
        {
            formData.Add("topt[15][min]", data.MinEngineVolume.Value.ToString());
        }
        if (data.MaxEngineVolume != null)
        {
            formData.Add("topt[15][max]", data.MaxEngineVolume.Value.ToString());
        }

        var content = new FormUrlEncodedContent(formData);

        HttpResponseMessage response = await client.PostAsync(SearchLink, content);

        return await response.Content.ReadAsStringAsync();
    }

    public List<string> ExtractLinks(string htmlContent)
    {
        List<string> links = new List<string>();
        HtmlDocument doc = new HtmlDocument();
        doc.LoadHtml(htmlContent);

        HtmlNodeCollection nodes = doc.DocumentNode.SelectNodes("//a[@class='am']");
        if (nodes != null)
        {
            foreach (HtmlNode node in nodes)
            {
                string partialLink = node.GetAttributeValue("href", "");
                links.Add($"https://www.ss.lv{partialLink}");
            }
        }
        return links;
    }


    public ScrapedCarData ParseSingleCarPage(string htmlContent, string carUrl)
    {
        HtmlDocument doc = new HtmlDocument();
        doc.LoadHtml(htmlContent);

        var carData = new ScrapedCarData { Url = carUrl };


        var descNode = doc.DocumentNode.SelectSingleNode("//div[@id='msg_div_msg']");

        if (descNode != null)
        {
            var scriptNodes = descNode.SelectNodes(".//script");
            if (scriptNodes != null)
            {
                foreach (var script in scriptNodes) script.Remove();
            }
            carData.Description = CleanText(descNode.InnerText);
        }

        var mainParamsNode = doc.DocumentNode.SelectSingleNode("//table[@class='options_list']");

        if (mainParamsNode != null)
        {
            carData.MainParameters = CleanText(mainParamsNode.InnerText);
        }

        var featuresNode = doc.DocumentNode.SelectSingleNode("//div[@id='msg_div_spec']");

        if (featuresNode != null)
        {
            carData.AdditionalFeatures = CleanText(featuresNode.InnerText);
        }

        var priceNode = doc.DocumentNode.SelectSingleNode("//span[@class='ads_price'] | //td[contains(@class, 'ads_price')] | //*[contains(text(), 'Цена:')]/following-sibling::*");

        if (priceNode != null)
        {
            string priceText = Regex.Replace(priceNode.InnerText, @"[^\d]", "");

            if (int.TryParse(priceText, out int parsedPrice))
            {
                carData.Price = parsedPrice;
            }
        }

        return carData;
    }

    private string CleanText(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        return Regex.Replace(input, @"\s+", " ").Trim();
    }
}
