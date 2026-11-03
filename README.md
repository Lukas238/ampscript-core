# AMPscript Core

AMPscript Core is an open source implementation of the Marketing Cloud AMPscript language.

It is cross platform and works on Windows, macOS and Linux.

A VSCode plugin is available to edit, run and debug AMPscript files locally on your machine.

<img src="docs/img/welcome.gif" alt="VSCode Plugin" width="50%"/>

## Getting Started Using the VSCode Plugin

The VSCode plugin provides syntax highlighting and debugging capability for AMPscript files.
The plugin supports files with a ".ampscript" extension.

It is not yet available on the VSCode marketplace. In order use it, you will need to download and install the VSIX file that contains the plugin.

#### Download and Install VSCode

If it isn't already installed on your computer, install Visual Studion Code (VS Code). You can download the latest version of VS Code from https://code.visualstudio.com/download.

#### Download and Install the .NET SDK

If it isn't already installed on your computer, install the .NET SDK version 8.0. You can download the SDK from https://dotnet.microsoft.com/en-us/download/dotnet.

#### Download the VSIX plugin

Download the latest VSIX release from the [releases](https://github.com/SalesforceLabs/ampscript-core/releases) tab.

Install the plugin using "Install From VSIX" in the extensions menu.

<img src="docs/img/install-vsix.png" alt="Install From VSIX" width="50%"/>

#### Unzip the example

Download the example ZIP from the [releases](https://github.com/SalesforceLabs/ampscript-core/releases) tab, and unzip this somewhere on your computer.

Do File->Open Folder and choose the folder where this was unzipped.

<img src="docs/img/open-folder.png" alt="Open Example Folder" width="50%"/>

#### Start the example

Double click on the "dd4tmp.ampscript" file, then go to the "Run and Debug" section of the editor and click "Run and Debug".

<img src="docs/img/open-file.png" alt="Open Root File" width="40%"/> <img src="docs/img/run-and-debug.png" alt="Open Root File" width="40%"/>

A browser window should appear with the example running. From here - you can make any changes to the example and reload your browser window.

<img src="docs/img/example-running.png" alt="Example Running" width="40%"/>

## Subscriber Attributes

Subscriber attributes `%%Foo%%`/`[Foo]` are the per-recipient values a real send would personalize with
(first name, plan tier, language, etc). Three ways to supply them, from simplest to most flexible:

1. one subscriber in `subscriber.json`
2. many subscribers in `_SUBSCRIBERS.csv`, switched by URL
3. one-off overrides via URL query string, layered on top of either

### 1. One subscriber: `subscriber.json`

Put a JSON file named `subscriber.json` in the working directory, with one key/value pair per attribute:

```json
{
    "FirstName": "Logan"
}
```

Reference it the same way a real send would:

```ampscript
%%[
    SET @FIRSTNAME = [FirstName]
]%%

Hello %%=V(@FIRSTNAME)=%%
or
Hello %%FirstName%%
```

Opening the debugger's root URL (`/`) renders using this file.

### 2. Many subscribers: `_SUBSCRIBERS.csv`

To test more than one subscriber without juggling multiple `subscriber.json` files, add a
`DataExtensions/_SUBSCRIBERS.csv` next to the entry file: semicolon-separated columns, a header row,
one row per subscriber. Headers must be nonempty and unique (case-insensitive); blank lines are skipped;
column padding is trimmed; values stay strings, including blanks and leading zeros.

```csv
SubscriberKey;FirstName;City
1;Jamie;Austin
2;Casey;Seattle
```

Request a 1-based row number in the URL - `/1` renders Jamie, `/2` renders Casey - instead of editing
`subscriber.json`. Open each numbered URL in its own tab to compare subscribers side by side. This is
request-local: `subscriber.json` is never read, merged, or modified by it, and the root `/` still uses
`subscriber.json` as before. Nonpositive row numbers return HTTP 400; missing rows/files return HTTP 404;
malformed CSV data returns HTTP 400 with details. Other side effects, such as custom functions that persist
DE inserts, still apply across requests regardless of which row you're previewing.

### 3. One-off overrides: URL query string

To check a single attribute change without adding a CSV row for it, add it to the query string of any
preview URL - root or numbered:

```text
/?LanguageCode=FR
/2?City=Chicago
/2?City=Chicago&LanguageCode=FR
```

Each `key=value` pair overrides (or adds) that attribute for this request only, on top of whatever
`subscriber.json` or CSV row was already selected; nothing is written back to disk. This is the fastest way
to spot-check copy across many values of one attribute (every language code, every tier, a boundary number)
without maintaining a CSV row for each.

## Referencing Content

Content functions can reference content local on disk. The files must exist in a `Content` subdirectory of the working directory.

Example, if you have a file at `Content\EmbeddedContent.ampscript`, then you can reference it via:

```ampscript
%%=CONTENTBLOCKBYNAME("EmbeddedContent")=%%
```

## Data Extensions

Data Extension support is provided through CSV files on disk. The files must exist in a `DataExtensions` subdirectory of the working directory. The CSV file must contain headers which represent the columns.

Example, if you have a file at `DataExtensions\Contacts.csv`, then it will use this CSV file as the data extension.

`DataExtensions\Contacts.csv`

```csv
EmailAddress,SubscriberKey,FirstName,LastName,City
jamie@example.com,1,Jamie,Rivera,Austin
```

`Index.ampscript`

```ampscript
Hello %%=LOOKUP("Contacts", "FirstName", "emailAddress", "jamie@example.com")=%%
```

Outputs:

```ampscript
Hello Jamie
```

## Custom Functions

If your AMPscript calls a function this engine doesn't natively support, compilation fails with a clear error instead of crashing - but you can patch that function locally with a `custom-functions.json` file in the working directory (next to your entry `.ampscript` file).

Each entry is keyed by the AMPscript function name (case-insensitive) and supports three modes:

- `"return"`: always returns this fixed literal value.
- `"echoArgument"`: returns one of the call's arguments (by zero-based index) unchanged.
- `"parameters"` + `"csharp"`: a real C# method body, compiled once at startup, with access to the named parameters, `context` (the current `RuntimeContext`), and `SageValue` conversion helpers - for actually implementing the function instead of stubbing it.

Example `custom-functions.json`:

```json
{
  "FORMATDATE": { "return": "2026-01-01" },
  "ENCRYPTSYMMETRIC": { "echoArgument": 0 },
  "DATEADD": {
    "parameters": ["date", "interval", "part"],
    "csharp": "var d = SageValue.ToDateTime(date, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal); return d.AddDays((double)SageValue.ToDecimal(interval)).ToString(\"yyyy-MM-dd\");"
  }
}
```

Functions that are already natively implemented always take priority over a `custom-functions.json` entry of the same name.

### ⚠️ AMPscript Support

| Feature                                             | Summary              |
| --------------------------------------------------- | -------------------- |
| Ampscript Blocks `%%[]%%`                           | ✔️                   |
| Variables                                           | ✔️                   |
| For `for`                                           | ✔️                   |
| If `if`                                             | ✔️                   |
| Logic Operators `==`, `!=`, `&&`, etc               | ✔️                   |
| Inline Ampscript `%%= =%%`                          | ✔️                   |
| Personalization Strings `[Foo]` or `%%Foo%%`        | ✔️                   |
| Tag Syntax `<script language="ampscript">`          | ✔️                   |
| System Strings `xtmonth, jobid, subscriberkey, etc` | ⛔ Not Yet Supported |

## Marketing Cloud Supported Languages

| Language  | Summary            |
| --------- | ------------------ |
| AMPscript | ⚠️ Partial support |
| SSJS      | ⛔ Not supported   |
| GTL       | ⛔ Not Supported   |

## Supported Functions

If the function isn't listed here, then it's not supported.
| Function Area | Supported / Notes |
|----------|---------|
|[Marketing Cloud API](https://ampscript.guide/marketing-cloud-api-functions/)| ⛔ Not Supported |
|[Contact Model](https://ampscript.guide/content-model-functions/)| ⛔ Not Supported |
|[Content](https://ampscript.guide/content-functions/)| ⚠️ Partial support |
|[Data Extensions](https://ampscript.guide/data-extension-functions/)| ⚠️ Partial Support |
|[Date and Time](https://ampscript.guide/date-and-time-functions/)| ⚠️ Partial Support |
|[Einstein](https://ampscript.guide/einstein-email-recommendation-functions/)| ⛔ Not Supported |
|[Encryption](https://ampscript.guide/encryption-and-encoding-functions/)| ⛔ Not Supported |
|[HTTP](https://ampscript.guide/http-functions/)| ⛔ Not Supported |
|[Math](https://ampscript.guide/math-functions/)| ✔️ |
|[Microsoft Dynamics](https://ampscript.guide/microsoft-dynamics-crm-functions/)| ⛔ Not Supported |
|[Sales and Service Cloud](https://ampscript.guide/sales-and-service-cloud-functions/)| ⛔ Not Supported |
|[Site](https://ampscript.guide/site-based-functions/)| ⛔ Not Supported |
|[Social](https://ampscript.guide/social-functions/)| ⛔ Not Supported |
|[String](https://ampscript.guide/string-functions/)| ⚠️ Partial Support |
|[Utility](https://ampscript.guide/utility-functions/)| ⚠️ Partial Support |
|[Content Syndication](https://ampscript.guide/content-syndication/)| ⛔ Not Supported |

## Roadmap

-   Increase supported feature
-   SSJS & GTL
-   Static code analysis tools to identify and remediate performance or security issues

## Docs

-   [Code of Conduct](./CODE_OF_CONDUCT.md)
-   [LICENSE](./LICENSE)
