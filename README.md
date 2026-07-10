AI Car Search SS.lv
A C# .NET console application that uses artificial intelligence (OpenAI API) to perform smart searches for vehicles on the Latvian classifieds portal SS.lv using natural language queries.

This project solves the problem of rigid manual filtering. Instead of manually selecting parameters in a web interface, the user describes the desired car in plain text. The system automatically extracts the search criteria, scrapes the website, and processes the final results through an LLM.

🛠 Tech Stack
Language: C# (.NET)

Scraping & Parsing: HttpClient, HtmlAgilityPack

AI Integration: OpenAI API (GPT-4o-mini)

Data Format: JSON

⚙️ Architecture and Workflow
The search process is divided into multiple stages to optimize HTTP requests and minimize API token usage:

1. Parameter Extraction (AI Stage 1)
The user inputs a text query (e.g., "I'm looking for a black BMW X5 or an Audi sedan under 5000 euros, newer than 2010").

The initial LLM call converts this text into a strictly typed JsonStructure object.

The system extracts base parameters (Manufacturer, Model, Year, Price) and secondary parameters (Color, Body type, Gearbox).

2. SS.lv Scraping (Parser Stage)
The program generates a target URL based on the extracted manufacturer and model (e.g., https://www.ss.lv/en/transport/cars/bmw/x5/filter/).

A POST request is sent to the SS.lv server, passing numerical filters (Year, Price) via FormUrlEncodedContent. This filters out a large volume of irrelevant listings directly on the server side.

The application parses the returned HTML to extract URLs for specific car listings.

The application executes GET requests for each collected link and uses XPath to extract the relevant text data: the seller's description, the basic parameters table, and the detailed specifications block.

3. Final Filtering (AI Stage 2)
The collected array of raw text from the listings is sent to the second LLM call.

The LLM analyzes the unstructured text to check for secondary filters (Color, Body type, specific options like A/C) that were saved during the first stage.

The application outputs the final list of verified URLs that strictly match the user's initial request.
