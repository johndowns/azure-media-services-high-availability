# High availability for Azure Media Services encoding

Azure Media Services runs within a single region. In the event of a regional outage or fault, requests to the encoding service within that region may fail. Solutions that require a high degree of fault tolerance may benefit from deploying their infrastructure across multiple regions. Azure Media Services does not provide monitoring of service faults, so the status of a given region's service must be inferred from the observed behaviour when submitting requests to the service.

This is an implementation of a variant of the [Scheduler Agent Supervisor pattern](https://docs.microsoft.com/en-us/azure/architecture/patterns/scheduler-agent-supervisor).

## Solution design

This solution wraps an Azure Media Services encoding job with a fault tolerance layer:

![Architecture diagram](diagram.png)

Media blobs that require encoding are uploaded to a regional ingestion blob storage container. The client must select a blob storage container to use, and may have multiple strategies for doing so such as round-robin (i.e. evenly distribute requests to all available blob containers) or regional affinity (i.e. send all requests to a storage account within a designated region, unless that storage account is unavailable).

As blobs are written to the storage account, an Event Grid trigger will fire to start an instance of an Azure Function. The function orchestrates a series of attempts to encode the media using Azure Media Services. The first attempt will use the Azure Media Services instance within the same region as the function app. If the attempt fails due to an error or times out, the function then selects an alternative Azure Media Services instance in another region to submit the request to.

Azure Media Services writes the output file to a blob storage container. If a client application requires notification for further processing, an [Event Grid subscription could be used to achieve this behaviour](https://docs.microsoft.com/en-us/azure/media-services/latest/reacting-to-media-services-events).

### Fault tolerance

The following types of outages are mitigated by this approach:

1. Azure Media Services regional outages and faults. In the event of a regional issue with Azure Media Services, the video processing orchestrator will eventually determine that the service is unhealthy and will route requests to an instance in the secondary region.
2. Blob storage regional outages and faults. In the event of a regional issue with Azure Storage, the client application will route the writing of blobs to the secondary region.

Furthermore, this approach allows for geo-distribution of the workloads. If the client application is aware of the locations of the regions, it can direct user traffic in the first instance to the closest or best-performing region.

## Assumptions

1. **Timeout calculation.** The expected processing time for a given media file must be calculated in order to detect abnormally long processing times. For simplicity this sample uses a fixed value of 10 minutes. However, a better approach would be to use historical data to calculate a metric such as the 99th percentile of the processing time for a media file of the specified length and format.

## Advantages and disadvantages

This approach has the following advantages:

1. **Horizontal scalability.** While this sample implements a solution with only two regions, it could easily be extended to retry processing across additional regions. In an extreme case, a single media encoding job could potentially be sent to instances of Azure Media Services within all supported Azure regions. By expressing all infrastructure as code, it is trivial to instantiate a new region, and minimal code changes would be required to add the new region into the pool of available regions for processing requests.

2. **Cacheability of endpoint status.** The client application can optionally maintain a cache of the current state of each raw media file blob storage instance and use this to direct subsequent requests to the best available endpoint, rather than attempting a request against an endpoint that is likely to be unsuccessful. Similarly, the video processing orchestrator function instances could optionally maintain a cache of the current state of each Azure Media Services instance.

However, the approach has a number of disadvantages and caveats:

1. **Delayed processing.** In the event of a problem with Azure Media Services that results in timeouts, it may take some time before a media file is attempted for reprocessing in a secondary region. If processing time and availability are critical for your solution, consider lowering the timeout threshold or even sending the job to multiple regions in the first instance rather than waiting for a problem. This will result in additional processing costs.

2. **Additional costs.** A highly available solution of this nature will result in additional costs. These can be broken into three categotries:
   * *Infrastructure:* Geo-replication of this nature requires application components to be deployed to multiple regions. This will increase the run cost of the solution. 
   * *Media processing:* The nature of this solution is that media may be processed multiple times, depending on the fault tolerance logic's determination of whether the primary region is available. Each processing request 
   * *Data egress:* If media files are stored within one region and processed in another, they may be subject to [Azure egress traffic pricing](https://azure.microsoft.com/en-us/pricing/details/bandwidth/).

3. **Limited scope.** This solution is not designed to provide high availability across every component of the solution. Single points of failure still exist, including the function app. Alternative approaches would be necessary to mitigate this risk.
