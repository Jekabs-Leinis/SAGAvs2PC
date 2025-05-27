# PowerShell script to build and push Docker images to AWS ECR

# --- Configuration ---
# !!! REPLACE THESE PLACEHOLDERS WITH YOUR ACTUAL VALUES !!!
$awsAccountId = "[REDACTED]"
$awsRegion = "eu-north-1"

# Service definitions: Name and Path to Dockerfile context (project root)
$services = @(
    @{ Name = "OrderService"; Path = "OrderService" },
    @{ Name = "PaymentService"; Path = "PaymentService" },
    @{ Name = "InventoryService"; Path = "InventoryService" },
    @{ Name = "CoordinationService"; Path = "CoordinationService" }
)

$ErrorActionPreference = "Stop" # Exit script on error

# --- AWS ECR Login ---
Write-Host "Logging in to AWS ECR..."
# Ensure AWS CLI is configured and you have permissions for ECR.
if ($awsAccountId -eq "YOUR_AWS_ACCOUNT_ID" -or $awsRegion -eq "YOUR_AWS_REGION") {
    Write-Error "Please replace AWS Account ID and Region placeholders in the script before running."
    exit 1
}

$ecrRegistry = "{0}.dkr.ecr.{1}.amazonaws.com" -f $awsAccountId, $awsRegion
aws ecr get-login-password --region $awsRegion | docker login --username AWS --password-stdin $ecrRegistry

if ($LASTEXITCODE -ne 0) {
    Write-Error "AWS ECR login failed. Please check your AWS CLI configuration and permissions."
    exit 1
}
Write-Host "ECR login successful."

# --- Build and Push Loop ---
foreach ($service in $services) {
    $serviceName = $service.Name
    $servicePath = $service.Path
    $imageTag = "latest" # You can use a more specific tag, e.g., based on git commit

    # ECR repository name is assumed to be the same as the service name (e.g., orderservice, paymentservice)
    # As per ECR naming conventions, repository names are typically lowercase.
    $ecrRepoName = $serviceName.ToLower()
    $ecrImageUri = "{0}/{1}:{2}" -f $ecrRegistry, $ecrRepoName, $imageTag

    Write-Host "`nProcessing service: $serviceName"
    Write-Host "Service Path: $PSScriptRoot\$servicePath"
    Write-Host "ECR Image URI: $ecrImageUri"

    # Ensure ECR repository exists
    Write-Host "Checking if ECR repository '$ecrRepoName' exists..."
    $oldErrorAction = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    $unused = aws ecr describe-repositories --repository-names $ecrRepoName --region $awsRegion 2>$null
    if ($LASTEXITCODE -ne 0) {
        Write-Host "ECR repository '$ecrRepoName' does not exist. Creating it..."
        $createRepoOutput = aws ecr create-repository --repository-name $ecrRepoName --region $awsRegion 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Failed to create ECR repository '$ecrRepoName'. AWS CLI output: $createRepoOutput"
            $ErrorActionPreference = $oldErrorAction
            continue
        }
        Write-Host "ECR repository '$ecrRepoName' created successfully."
    } else {
        Write-Host "ECR repository '$ecrRepoName' already exists."
    }
    $ErrorActionPreference = $oldErrorAction

    # Navigate to service directory
    Push-Location "$PSScriptRoot\$servicePath"
    
    # Build Docker image
    Write-Host "Building Docker image for $serviceName..."
    docker build -t "$($serviceName.ToLower()):$imageTag" .
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Docker build failed for $serviceName."
        Pop-Location
        continue # Or exit 1 based on preference
    }
    Write-Host "Docker image for $serviceName built successfully."

    # Tag Docker image for ECR
    Write-Host "Tagging image $($serviceName.ToLower()):$imageTag as $ecrImageUri..."
    docker tag "$($serviceName.ToLower()):$imageTag" $ecrImageUri
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Docker tag failed for $serviceName."
        Pop-Location
        continue
    }
    Write-Host "Image tagged successfully for ECR."

    # Push Docker image to ECR
    Write-Host "Pushing image $ecrImageUri to ECR..."
    docker push $ecrImageUri
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Docker push failed for $serviceName to $ecrImageUri."
        Pop-Location
        continue
    }
    Write-Host "Image $ecrImageUri pushed successfully to ECR."

    # Return to original script location
    Pop-Location
}

Write-Host "`nAll services processed." 

