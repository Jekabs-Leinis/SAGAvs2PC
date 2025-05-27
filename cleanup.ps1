[CmdletBinding()]
param(
    [string]$EnvironmentName = "dev",
    [string]$AwsRegion = "eu-north-1",
    [string]$AwsAccountId = "[REDACTED]",
    [switch]$DryRun
)

$ErrorActionPreference = "Continue"

# Resource names
$stackName = "$EnvironmentName-MicroservicesStack"
$ecrRepos = @("orderservice", "paymentservice", "inventoryservice", "coordinationservice")

Write-Host "Cleanup script started for environment: $EnvironmentName"
if ($DryRun) {
    Write-Host "Running in DryRun mode. No resources will be deleted."
}

# --- CloudFormation Stack ---
Write-Host "`nChecking CloudFormation stack: $stackName"
$stack = aws cloudformation describe-stacks --stack-name $stackName --region $AwsRegion 2>$null
if ($LASTEXITCODE -eq 0) {
    if ($DryRun) {
        Write-Host "Would delete CloudFormation stack: $stackName"
    } else {
        Write-Host "Deleting CloudFormation stack: $stackName"
        aws cloudformation delete-stack --stack-name $stackName --region $AwsRegion
        Write-Host "Delete initiated for stack: $stackName"
    }
} else {
    Write-Host "CloudFormation stack $stackName does not exist or already deleted."
}

# --- ECR Repositories ---
foreach ($repo in $ecrRepos) {
    Write-Host "`nChecking ECR repository: $repo"
    $repoDesc = aws ecr describe-repositories --repository-names $repo --region $AwsRegion 2>$null
    if ($LASTEXITCODE -eq 0) {
        if ($DryRun) {
            Write-Host "Would delete ECR repository: $repo (including all images)"
        } else {
            Write-Host "Deleting all images in ECR repository: $repo"
            $images = aws ecr list-images --repository-name $repo --region $AwsRegion --query 'imageIds[*]' --output json | ConvertFrom-Json
            if ($images.Count -gt 0) {
                # Convert $images to valid JSON for AWS CLI
                $jsonImages = $images | ConvertTo-Json -Compress
                aws ecr batch-delete-image --repository-name $repo --region $AwsRegion --image-ids "$jsonImages"
            }
            Write-Host "Deleting ECR repository: $repo"
            aws ecr delete-repository --repository-name $repo --region $AwsRegion --force
        }
    } else {
        Write-Host "ECR repository $repo does not exist or already deleted."
    }
}

# --- Local Docker Images (optional) ---
Write-Host "`nChecking for local Docker images to remove..."
foreach ($repo in $ecrRepos) {
    $localImages = docker images --format "{{.Repository}}:{{.Tag}}" | Where-Object { $_ -like "${repo}:*" }
    if ($localImages) {
        foreach ($img in $localImages) {
            if ($DryRun) {
                Write-Host "Would remove local Docker image: $img"
            } else {
                Write-Host "Removing local Docker image: $img"
                docker rmi $img 2>$null
            }
        }
    } else {
        Write-Host "No local Docker images found for $repo"
    }
}

Write-Host "`nCleanup script completed."
if ($DryRun) {
    Write-Host "No resources were deleted due to DryRun mode."
}
