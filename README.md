# Meeting Summarization App

## Overview

The Meeting Summarization App is designed to transcribe and summarize meeting recordings using Azure Durable Functions and AI services. This application automates the process of converting audio files into text, identifying key points, and generating a concise summary of the meeting content.

## Architecture

![Architecture](./documents/dataflow-architecture.png)

## Features

- **Audio Transcription**: Converts audio recordings into text using Azure Cognitive Services.
- **Summarization**: Uses AI models to summarize transcribed text, highlighting key points and action items.
- **Scalability**: Leverages Azure Durable Functions to handle large volumes of data and complex workflows efficiently.
- **Storage**: Stores transcriptions and summaries in Azure Blob Storage for easy access and retrieval.

## Prerequisites

- Azure Subscription
- Azure Functions Core Tools
- .NET SDK
- Azure Storage Account
- Pulumi
