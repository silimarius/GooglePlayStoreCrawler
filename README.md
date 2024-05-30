# Google Play Store Crawler

A program to retrieve and store 1000+ apps metadata from Play Store.

## Prerequisites

In order to build and run the program you need to:

1. Download and install [.NET Core 3.1 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/3.1)
2. From the project root directory run `dotnet restore` to download all the NuGet packages

## Build and run

Make sure you are in the project root directory and run the following command to build the program:

```sh
dotnet build
```

To run the program run:

```sh
dotnet run
```

## Reading the results

The retrieved applications' metadata is stored in the `./apps_metadata` directory in a JSON format. Since not every application provides an aggregated rating, apps with ratings are also stored in `./rated_apps_metadata` directory. In case you can't find these directories locally (maybe because you ran the process from another directory), read the logs to check the global paths.

If you ever get tired of your metadata, you can always run:

```sh
rm -rf ./**/*apps_metadata/
```
