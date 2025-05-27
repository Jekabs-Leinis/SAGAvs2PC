[CmdletBinding()]
param (

    [string]$AwsAccountId = "[REDACTED]",
    [string]$AwsRegion = "eu-north-1",
    [string]$EnvironmentName, # e.g., "dev", "staging", "prod"

    [string]$OrderServiceImageTag = "latest",
    [string]$PaymentServiceImageTag = "latest",
    [string]$InventoryServiceImageTag = "latest",
    [string]$CoordinationServiceImageTag = "latest",

    # RDS Parameters (defaults can be changed here or overridden via command line)
    [string]$RDSInstanceClass = "db.t3.micro",
    [string]$RDSAllocatedStorage = "20",
    [string]$RDSEngineVersion = "8.0.35", # Ensure this matches supported versions
    [string]$RDSMasterUsername = "admin",
    [string]$RDSMasterUserPassword = "AT7mM3d3", # <-- Add this line

    # VPC Parameters
    [string]$VpcCIDR = "10.0.0.0/16",
    [string]$PublicSubnet1CIDR = "10.0.1.0/24",
    [string]$PublicSubnet2CIDR = "10.0.2.0/24",
    [string]$PrivateSubnet1CIDR = "10.0.3.0/24",
    [string]$PrivateSubnet2CIDR = "10.0.4.0/24",

    # SSM KMS Key ID (optional, defaults in template to alias/aws/ssm)
    [string]$SsmKmsKeyId = "", # Leave empty to use default, or specify a KMS Key ID

    # ECS Service Parameters (defaults)
    [string]$OrderServiceDesiredCount = "1",
    [string]$OrderServiceCpu = "256",
    [string]$OrderServiceMemory = "512",
    [string]$PaymentServiceDesiredCount = "1",
    [string]$PaymentServiceCpu = "256",
    [string]$PaymentServiceMemory = "512",
    [string]$InventoryServiceDesiredCount = "1",
    [string]$InventoryServiceCpu = "256",
    [string]$InventoryServiceMemory = "512",
    [string]$CoordinationServiceDesiredCount = "1",
    [string]$CoordinationServiceCpu = "256",
    [string]$CoordinationServiceMemory = "512"
)

# --- IMPORTANT: FILL IN YOUR AWS ACCOUNT ID AND REGION BELOW ---
if ($AwsAccountId -eq "YOUR_AWS_ACCOUNT_ID" -or [string]::IsNullOrWhiteSpace($AwsAccountId)) {
    Write-Error "Please replace 'YOUR_AWS_ACCOUNT_ID' with your actual AWS Account ID in the script parameters or provide it when running the script."
    exit 1
}

if ($AwsRegion -eq "YOUR_AWS_REGION" -or [string]::IsNullOrWhiteSpace($AwsRegion)) {
    Write-Error "Please replace 'YOUR_AWS_REGION' with your actual AWS Region in the script parameters or provide it when running the script."
    exit 1
}
# --- END IMPORTANT SECTION ---

# Prompt for RDSMasterUserPassword if not provided
if (-not $RDSMasterUserPassword) {
    $RDSMasterUserPassword = Read-Host -AsSecureString "Enter RDS Master User Password"
    # Convert SecureString to plain text for AWS CLI (ensure script is not logged)
    $BSTR = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($RDSMasterUserPassword)
    $RDSMasterUserPassword = [System.Runtime.InteropServices.Marshal]::PtrToStringBSTR($BSTR)
    [System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($BSTR)
}

$StackName = "$EnvironmentName-MicroservicesStack"
$TemplateFile = "./main-cfn-template.yaml"

# --- Remove existing stack if it exists ---
$stackExists = $false
try {
    $describe = aws cloudformation describe-stacks --stack-name $StackName --region $AwsRegion 2>$null
    if ($describe) {
        $stackExists = $true
    }
} catch {
    $stackExists = $false
}

if ($stackExists) {
    Write-Host "Stack $StackName exists. Deleting it before deployment..."
    aws cloudformation delete-stack --stack-name $StackName --region $AwsRegion
    Write-Host "Waiting for stack deletion to complete..."
    aws cloudformation wait stack-delete-complete --stack-name $StackName --region $AwsRegion
    Write-Host "Stack $StackName deleted."
}

# Construct parameter overrides string
$ParameterOverrides = @(
    "EnvironmentName=$EnvironmentName",
    "AwsAccountId=$AwsAccountId",
    "AwsRegion=$AwsRegion",
    "OrderServiceImageTag=$OrderServiceImageTag",
    "PaymentServiceImageTag=$PaymentServiceImageTag",
    "InventoryServiceImageTag=$InventoryServiceImageTag",
    "CoordinationServiceImageTag=$CoordinationServiceImageTag",
    "RDSInstanceClass=$RDSInstanceClass",
    "RDSAllocatedStorage=$RDSAllocatedStorage",
    "RDSEngineVersion=$RDSEngineVersion",
    "RDSMasterUsername=$RDSMasterUsername",
    "RDSMasterUserPassword=$RDSMasterUserPassword",
    "DatabasePassword=$RDSMasterUserPassword", # <-- Add this line
    "VpcCIDR=$VpcCIDR",
    "PublicSubnet1CIDR=$PublicSubnet1CIDR",
    "PublicSubnet2CIDR=$PublicSubnet2CIDR",
    "PrivateSubnet1CIDR=$PrivateSubnet1CIDR",
    "PrivateSubnet2CIDR=$PrivateSubnet2CIDR",
    "OrderServiceDesiredCount=$OrderServiceDesiredCount",
    "OrderServiceCpu=$OrderServiceCpu",
    "OrderServiceMemory=$OrderServiceMemory",
    "PaymentServiceDesiredCount=$PaymentServiceDesiredCount",
    "PaymentServiceCpu=$PaymentServiceCpu",
    "PaymentServiceMemory=$PaymentServiceMemory",
    "InventoryServiceDesiredCount=$InventoryServiceDesiredCount",
    "InventoryServiceCpu=$InventoryServiceCpu",
    "InventoryServiceMemory=$InventoryServiceMemory",
    "CoordinationServiceDesiredCount=$CoordinationServiceDesiredCount",
    "CoordinationServiceCpu=$CoordinationServiceCpu",
    "CoordinationServiceMemory=$CoordinationServiceMemory"
)

if (-not [string]::IsNullOrWhiteSpace($SsmKmsKeyId)) {
    $ParameterOverrides += "SsmKmsKeyId=$SsmKmsKeyId"
}

$ParameterOverridesString = $ParameterOverrides -join " "

# AWS CLI command to deploy the CloudFormation stack
$CommandParts = @(
    "aws cloudformation deploy"
    "--template-file $TemplateFile"
    "--stack-name $StackName"
    "--capabilities CAPABILITY_IAM CAPABILITY_NAMED_IAM"
    "--region $AwsRegion"
    "--parameter-overrides $ParameterOverridesString"
)
$Command = $CommandParts -join " "

Write-Host "Executing CloudFormation deployment command:"
Write-Host $Command

# Execute the command
Invoke-Expression $Command

if ($LASTEXITCODE -eq 0) {
    Write-Host "CloudFormation stack deployment initiated successfully for stack: $StackName"
    Write-Host "You can monitor the deployment status in the AWS CloudFormation console."
    Write-Host "Run the following command to get stack outputs (after completion):"
    Write-Host "aws cloudformation describe-stacks --stack-name $StackName --query 'Stacks[0].Outputs' --output table --region $AwsRegion"
} else {
    Write-Error "CloudFormation stack deployment failed. Check the AWS CloudFormation console for details."
} 
