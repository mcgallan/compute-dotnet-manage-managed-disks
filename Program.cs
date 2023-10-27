// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.Compute;
using Azure.ResourceManager.Compute.Models;
using Azure.ResourceManager.Network;
using Azure.ResourceManager.Network.Models;
using Azure.ResourceManager.Resources;
using Azure.ResourceManager.Samples.Common;

namespace ManageManagedDisks
{
    public class Program
    {
        /**
         * This is sample will not be published, this is just to ensure out blog is honest.
         */

        public static async Task RunSample(ArmClient client)
        {
            var region = AzureLocation.EastUS;
            var rgName = Utilities.CreateRandomName("rgCOMV");
            var userName = Utilities.CreateUsername();
            var password = Utilities.CreatePassword();
            var publicIpDnsLabel = Utilities.CreateRandomName("pip" + "-");
            var networkName = Utilities.CreateRandomName("VirtualNetwork_");
            var sshKey = "ssh-rsa AAAAB3NzaC1yc2EAAAADAQABAAABAQCfSPC2K7LZcFKEO+/t3dzmQYtrJFZNxOsbVgOVKietqHyvmYGHEC0J2wPdAqQ/63g/hhAEFRoyehM+rbeDri4txB3YFfnOK58jqdkyXzupWqXzOrlKY4Wz9SKjjN765+dqUITjKRIaAip1Ri137szRg71WnrmdP3SphTRlCx1Bk2nXqWPsclbRDCiZeF8QOTi4JqbmJyK5+0UqhqYRduun8ylAwKKQJ1NJt85sYIHn9f1Rfr6Tq2zS0wZ7DHbZL+zB5rSlAr8QyUdg/GQD+cmSs6LvPJKL78d6hMGk84ARtFo4A79ovwX/Fj01znDQkU6nJildfkaolH2rWFG/qttD azjava@javalib.Com";
            var lro = await client.GetDefaultSubscription().GetResourceGroups().CreateOrUpdateAsync(Azure.WaitUntil.Completed, rgName, new ResourceGroupData(AzureLocation.EastUS));
            var resourceGroup = lro.Value;
            try
            {
                // ::==Create a VM
                // Create a virtual machine with an implicit Managed OS disk and explicit Managed data disk

                Utilities.Log("Creating VM [with an implicit Managed OS disk and explicit Managed data disk]");

                var linuxVM1Name = Utilities.CreateRandomName("vm" + "-");
                var linuxVM1Pip = Utilities.CreateRandomName("pip" + "-");
                var linuxComputerName = Utilities.CreateRandomName("linuxComputer");
                var networkCollection = resourceGroup.GetVirtualNetworks();
                var networkData = new VirtualNetworkData()
                {
                    AddressPrefixes =
                    {
                        "10.0.0.0/28"
                    }
                };
                var publicIpAddressCollection = resourceGroup.GetPublicIPAddresses();
                var publicIPAddressData = new PublicIPAddressData()
                {
                    DnsSettings =
                            {
                                DomainNameLabel = publicIpDnsLabel
                            }
                };
                var publicIpAddressCreatable = (publicIpAddressCollection.CreateOrUpdate(Azure.WaitUntil.Completed, linuxVM1Pip, publicIPAddressData)).Value;
                var networkCreatable = networkCollection.CreateOrUpdate(Azure.WaitUntil.Completed, networkName, networkData).Value;
                var subnetName = Utilities.CreateRandomName("subnet_");
                var subnetData = new SubnetData()
                {
                    ServiceEndpoints =
                    {
                        new ServiceEndpointProperties()
                        {
                            Service = "Microsoft.Storage"
                        }
                    },
                    Name = subnetName,
                    AddressPrefix = "10.0.0.0/28",
                };
                var subnetLRro = networkCreatable.GetSubnets().CreateOrUpdate(WaitUntil.Completed, subnetName, subnetData);
                var subnet = subnetLRro.Value;
                var networkInterfaceData = new NetworkInterfaceData()
                {
                    Location = AzureLocation.EastUS,
                    IPConfigurations =
                    {
                        new NetworkInterfaceIPConfigurationData()
                        {
                            Name = "internal",
                            Primary = true,
                            Subnet = new SubnetData
                            {
                                Name = subnetName,
                                Id = new ResourceIdentifier($"{networkCreatable.Data.Id}/subnets/{subnetName}")
                            },
                            PrivateIPAllocationMethod = NetworkIPAllocationMethod.Dynamic,
                            PublicIPAddress = publicIpAddressCreatable.Data,
                        }
                    }
                };
                var networkInterfaceName = Utilities.CreateRandomName("networkInterface");
                var nic = (resourceGroup.GetNetworkInterfaces().CreateOrUpdate(WaitUntil.Completed, networkInterfaceName, networkInterfaceData)).Value;
                var vmCollection = resourceGroup.GetVirtualMachines();
                var linuxVmdata1 = new VirtualMachineData(AzureLocation.EastUS)
                {
                    HardwareProfile = new VirtualMachineHardwareProfile()
                    {
                        VmSize = "Standard_D2a_v4"
                    },
                    OSProfile = new VirtualMachineOSProfile()
                    {
                        AdminUsername = userName,
                        AdminPassword = password,
                        ComputerName = linuxComputerName,
                        LinuxConfiguration = new LinuxConfiguration()
                        {
                            SshPublicKeys =
                                {
                                    new SshPublicKeyConfiguration()
                                    {
                                        KeyData = sshKey,
                                        Path = $"/home/{userName}/.ssh/authorized_keys"
                                    }
                                }
                        }
                    },
                    NetworkProfile = new VirtualMachineNetworkProfile()
                    {
                        NetworkInterfaces =
                        {
                            new VirtualMachineNetworkInterfaceReference()
                            {
                                Id = nic.Id,
                                Primary = true,
                            }
                        }
                    },
                    StorageProfile = new VirtualMachineStorageProfile()
                    {
                        ImageReference = new ImageReference()
                        {
                            Publisher = "Canonical",
                            Offer = "UbuntuServer",
                            Sku = "16.04-LTS",
                            Version = "latest",
                        },
                    },
                };
                var virtualMachineResource1 = vmCollection.CreateOrUpdate(WaitUntil.Completed, linuxVM1Name, linuxVmdata1).Value;

                Utilities.Log("Created VM [with an implicit Managed OS disk and explicit Managed data disk]");

                // Creation is simplified with implicit creation of managed disks without specifying all the disk details. You will notice that you do not require storage accounts
                // ::== Update the VM
                // Create a VMSS with implicit managed OS disks and explicit managed data disks

                Utilities.Log("Creating VMSS [with implicit managed OS disks and explicit managed data disks]");

                var vmScaleSetName = Utilities.CreateRandomName("vmss" + "-");
                var vmScaleSet = azure.VirtualMachineScaleSets
                        .Define(vmScaleSetName)
                        .WithRegion(region)
                        .WithExistingResourceGroup(rgName)
                        .WithSku(VirtualMachineScaleSetSkuTypes.StandardD5v2)
                        .WithExistingPrimaryNetworkSubnet(PrepareNetwork(azure, region, rgName), "subnet1")
                        .WithExistingPrimaryInternetFacingLoadBalancer(PrepareLoadBalancer(azure, region, rgName))
                        .WithoutPrimaryInternalLoadBalancer()
                        .WithPopularLinuxImage(KnownLinuxVirtualMachineImage.UbuntuServer16_04_Lts)
                        .WithRootUsername(Utilities.CreateUsername())
                        .WithSsh(sshkey)
                        .WithNewDataDisk(100)
                        .WithNewDataDisk(100, 1, CachingTypes.ReadWrite)
                        .WithNewDataDisk(100, 2, CachingTypes.ReadOnly)
                        .WithCapacity(3)
                        .Create();

                Utilities.Log("Created VMSS [with implicit managed OS disks and explicit managed data disks]");

                // Create an empty disk and attach to a VM (Manage Virtual Machine With Disk)

                Utilities.Log("Creating empty data disk [to attach to a VM]");

                var diskName = Utilities.CreateRandomName("dsk" + "-");
                var diskCollection = resourceGroup.GetManagedDisks();
                var diskData = new ManagedDiskData(region)
                {
                    DiskSizeGB = 50
                };
                var disk = (await diskCollection.CreateOrUpdateAsync(WaitUntil.Completed, diskName, diskData)).Value;

                Utilities.Log("Created empty data disk [to attach to a VM]");

                Utilities.Log("Creating VM [with new managed data disks and disk attached]");

                var linuxVM2Name = Utilities.CreateRandomName("vm" + "-");
                var linuxVM2Pip = Utilities.CreateRandomName("pip" + "-");
                var linuxVmdata2 = new VirtualMachineData(AzureLocation.EastUS)
                {
                    HardwareProfile = new VirtualMachineHardwareProfile()
                    {
                        VmSize = "Standard_D2a_v4"
                    },
                    OSProfile = new VirtualMachineOSProfile()
                    {
                        AdminUsername = userName,
                        AdminPassword = password,
                        ComputerName = linuxComputerName,
                        LinuxConfiguration = new LinuxConfiguration()
                        {
                            SshPublicKeys =
                                {
                                    new SshPublicKeyConfiguration()
                                    {
                                        KeyData = sshKey,
                                        Path = $"/home/{userName}/.ssh/authorized_keys"
                                    }
                                }
                        }
                    },
                    NetworkProfile = new VirtualMachineNetworkProfile()
                    {
                        NetworkInterfaces =
                        {
                            new VirtualMachineNetworkInterfaceReference()
                            {
                                Id = nic.Id,
                                Primary = true,
                            }
                        }
                    },
                    StorageProfile = new VirtualMachineStorageProfile()
                    {
                        OSDisk = new VirtualMachineOSDisk(DiskCreateOptionType.FromImage)
                        {
                            Caching = CachingType.ReadWrite,
                        },
                        DataDisks =
                    {
                        new VirtualMachineDataDisk(1, DiskCreateOptionType.FromImage)
                        {
                            DiskSizeGB = 100,
                        },
                        new VirtualMachineDataDisk(2, DiskCreateOptionType.FromImage)
                        {
                            DiskSizeGB = 100
                        },
                    },
                        ImageReference = new ImageReference()
                        {
                            Publisher = "Canonical",
                            Offer = "UbuntuServer",
                            Sku = "16.04-LTS",
                            Version = "latest",
                        },
                    },
                };
                var virtualMachineResource2 = vmCollection.CreateOrUpdate(WaitUntil.Completed, linuxVM2Name, linuxVmdata2).Value;

                Utilities.Log("Created VM [with new managed data disks and disk attached]");

                // Update a VM

                Utilities.Log("Updating VM [by detaching a disk and adding empty disk]");

                await virtualMachineResource2.UpdateAsync(WaitUntil.Completed, new VirtualMachinePatch()
                {
                    StorageProfile = new VirtualMachineStorageProfile()
                    {
                        DataDisks =
                        {
                            new VirtualMachineDataDisk(1, DiskCreateOptionType.Attach)
                            {
                                DiskSizeGB = 100,
                            },
                            new VirtualMachineDataDisk(3, DiskCreateOptionType.Attach)
                            {
                                DiskSizeGB = 200
                            }
                        }
                    }
                });

                Utilities.Log("Updated VM [by detaching a disk and adding empty disk]");

                // Create a VM from an image (Create Virtual Machine Using Custom Image from VM)

                Utilities.Log("Preparing specialized virtual machine with un-managed disk");

                var linuxVM = PrepareSpecializedUnmanagedVirtualMachine(resourceGroup);

                Utilities.Log("Prepared specialized virtual machine with un-managed disk");

                Utilities.Log("Creating custom image from specialized virtual machine");

                var galleryeName = Utilities.CreateRandomName("gallery");
                var customImageName = Utilities.CreateRandomName("cimg" + "-");
                var galleryCollection = resourceGroup.GetGalleries();
                var galleryData = new GalleryData(region)
                {
                    Description = "gallery Image"
                };
                var galleryResource = (await galleryCollection.CreateOrUpdateAsync(WaitUntil.Completed, galleryeName, galleryData)).Value;
                var customImageCollection = galleryResource.GetGalleryImages();
                var imageData = new GalleryImageData(region)
                {
                    OSType = SupportedOperatingSystemType.Linux
                };
                var imageResource = (await customImageCollection.CreateOrUpdateAsync(WaitUntil.Completed, customImageName, imageData)).Value;

                Utilities.Log("Created custom image from specialized virtual machine");

                Utilities.Log("Creating VM [from custom image]");

                var linuxVM3Name = Utilities.CreateRandomName("vm" + "-");
                var linuxVmdata3 = new VirtualMachineData(AzureLocation.EastUS)
                {
                    HardwareProfile = new VirtualMachineHardwareProfile()
                    {
                        VmSize = "Standard_D2a_v4"
                    },
                    OSProfile = new VirtualMachineOSProfile()
                    {
                        ComputerName = linuxComputerName,
                        LinuxConfiguration = new LinuxConfiguration()
                        {
                            SshPublicKeys =
                                {
                                    new SshPublicKeyConfiguration()
                                    {
                                        KeyData = sshKey,
                                        Path = $"/home/{userName}/.ssh/authorized_keys"
                                    }
                                }
                        }
                    },
                    NetworkProfile = new VirtualMachineNetworkProfile()
                    {
                        NetworkInterfaces =
                        {
                            new VirtualMachineNetworkInterfaceReference()
                            {
                                Id = nic.Id,
                                Primary = true,
                            }
                        }
                    },
                    StorageProfile = new VirtualMachineStorageProfile()
                    {
                        ImageReference = new ImageReference()
                        {
                            Publisher = "Canonical",
                            Offer = "UbuntuServer",
                            Sku = "16.04-LTS",
                            Version = "latest",
                            CommunityGalleryImageId = imageResource.Id,
                        },
                    },
                };
                var virtualMachineResource3 = vmCollection.CreateOrUpdate(WaitUntil.Completed, linuxVM3Name, linuxVmdata3).Value;

                Utilities.Log("Created VM [from custom image]");

                // Create a VM from a VHD (Create Virtual Machine Using Specialized VHD)

                var linuxVmName4 = Utilities.CreateRandomName("vm" + "-");
                var specializedVhd = virtualMachineResource1.Data.StorageProfile.OSDisk.VhdUri;
                await virtualMachineResource1.DeleteAsync(WaitUntil.Completed);

                Utilities.Log("Creating VM [by attaching un-managed disk]");
                var linuxVmdata4 = new VirtualMachineData(AzureLocation.EastUS)
                {
                    HardwareProfile = new VirtualMachineHardwareProfile()
                    {
                        VmSize = "Standard_D2a_v4"
                    },
                    OSProfile = new VirtualMachineOSProfile()
                    {
                        ComputerName = linuxComputerName,
                        LinuxConfiguration = new LinuxConfiguration()
                        {
                            SshPublicKeys =
                                {
                                    new SshPublicKeyConfiguration()
                                    {
                                        KeyData = sshKey,
                                        Path = $"/home/{userName}/.ssh/authorized_keys"
                                    }
                                }
                        }
                    },
                    NetworkProfile = new VirtualMachineNetworkProfile()
                    {
                        NetworkInterfaces =
                        {
                            new VirtualMachineNetworkInterfaceReference()
                            {
                                Id = nic.Id,
                                Primary = true,
                            }
                        }
                    },
                    StorageProfile = new VirtualMachineStorageProfile()
                    {
                        OSDisk = new VirtualMachineOSDisk(DiskCreateOptionType.Attach)
                        {
                            VhdUri = specializedVhd,
                            OSType = SupportedOperatingSystemType.Linux
                        }
                    },
                };
                var virtualMachineResource4 = vmCollection.CreateOrUpdate(WaitUntil.Completed, linuxVmName4, linuxVmdata4).Value;

                Utilities.Log("Created VM [by attaching un-managed disk]");

                // Create a Snapshot (Create Virtual Machine Using Specilaized Disks from Snapshot)

                Utilities.Log("Preparing specialized virtual machine with managed disks");

                var linuxVM5 = PrepareSpecializedManagedVirtualMachine(region, resourceGroup);
                var osDisk = linuxVM5.Data.StorageProfile.OSDisk.ManagedDisk.Id;
                var dataDisks = new List<VirtualMachineDataDisk>();
                foreach (var dataDisk in linuxVM5.Data.StorageProfile.DataDisks)
                {
                    dataDisks.Add(dataDisk);
                }

                Utilities.Log("Prepared specialized virtual machine with managed disks");

                Utilities.Log("Deleting VM: " + linuxVM5.Id);
                await linuxVM5.DeleteAsync(WaitUntil.Completed);
                Utilities.Log("Deleted the VM: " + linuxVM5.Id);

                Utilities.Log("Creating snapshot [from managed OS disk]");

                // Create a managed snapshot for an OS disk
                var managedOSSnapshotName = Utilities.CreateRandomName("snp" + "-");
                var snpashotCollection = resourceGroup.GetSnapshots();
                var snpData = new SnapshotData(region)
                {
                    DiskAccessId = osDisk,
                };
                var osSnapshot = await snpashotCollection.CreateOrUpdateAsync(WaitUntil.Completed, managedOSSnapshotName, snpData);

                Utilities.Log("Created snapshot [from managed OS disk]");

                Utilities.Log("Creating managed OS disk [from snapshot]");

                // Create a managed disk from the managed snapshot for the OS disk
                var managedNewOSDiskName = Utilities.CreateRandomName("dsk" + "-");
                var newOSDisk = azure.Disks.Define(managedNewOSDiskName)
                        .WithRegion(region)
                        .WithExistingResourceGroup(rgName)
                        .WithLinuxFromSnapshot(osSnapshot)
                        .WithSizeInGB(100)
                        .Create();

                Utilities.Log("Created managed OS disk [from snapshot]");

                Utilities.Log("Creating managed data snapshot [from managed data disk]");

                // Create a managed snapshot for a data disk
                var managedDataDiskSnapshotName = Utilities.CreateRandomName("dsk" + "-");
                var dataSnapshot = azure.Snapshots.Define(managedDataDiskSnapshotName)
                        .WithRegion(region)
                        .WithExistingResourceGroup(rgName)
                        .WithDataFromDisk(dataDisks[0])
                        .WithSku(DiskSkuTypes.StandardLRS)
                        .Create();

                Utilities.Log("Created managed data snapshot [from managed data disk]");

                Utilities.Log("Creating managed data disk [from managed snapshot]");

                // Create a managed disk from the managed snapshot for the data disk
                var managedNewDataDiskName = Utilities.CreateRandomName("dsk" + "-");
                var newDataDisk = azure.Disks.Define(managedNewDataDiskName)
                        .WithRegion(region)
                        .WithExistingResourceGroup(rgName)
                        .WithData()
                        .FromSnapshot(dataSnapshot)
                        .Create();

                Utilities.Log("Created managed data disk [from managed snapshot]");

                Utilities.Log("Creating VM [with specialized OS managed disk]");

                var linuxVm6Name = Utilities.CreateRandomName("vm" + "-");
                var linuxVM6 = azure.VirtualMachines.Define(linuxVm6Name)
                        .WithRegion(region)
                        .WithExistingResourceGroup(rgName)
                        .WithNewPrimaryNetwork("10.0.0.0/28")
                        .WithPrimaryPrivateIPAddressDynamic()
                        .WithoutPrimaryPublicIPAddress()
                        .WithSpecializedOSDisk(newOSDisk, OperatingSystemTypes.Linux)
                        .WithExistingDataDisk(newDataDisk)
                        .WithSize(VirtualMachineSizeTypes.Parse("Standard_D2a_v4"))
                        .Create();

                Utilities.Log("Created VM [with specialized OS managed disk]");

                // ::== Migrate a VM to managed disks with a single reboot

                Utilities.Log("Creating VM [with un-managed disk for migration]");

                var linuxVM7Name = Utilities.CreateRandomName("vm" + "-");
                var linuxVM7Pip = Utilities.CreateRandomName("pip" + "-");
                var linuxVM7 = azure.VirtualMachines.Define(linuxVM7Name)
                        .WithRegion(region)
                        .WithNewResourceGroup(rgName)
                        .WithNewPrimaryNetwork("10.0.0.0/28")
                        .WithPrimaryPrivateIPAddressDynamic()
                        .WithNewPrimaryPublicIPAddress(linuxVM7Pip)
                        .WithPopularLinuxImage(KnownLinuxVirtualMachineImage.UbuntuServer16_04_Lts)
                        .WithRootUsername(Utilities.CreateUsername())
                        .WithSsh(sshkey)
                        .WithUnmanagedDisks() // uses storage accounts
                        .WithNewUnmanagedDataDisk(100)
                        .WithSize(VirtualMachineSizeTypes.Parse("Standard_D2a_v4"))
                        .Create();

                Utilities.Log("Created VM [with un-managed disk for migration]");

                Utilities.Log("De-allocating VM :" + linuxVM7.Id);

                linuxVM7.Deallocate();

                Utilities.Log("De-allocated VM :" + linuxVM7.Id);

                Utilities.Log("Migrating VM");

                linuxVM7.ConvertToManaged();

                Utilities.Log("Migrated VM");
            }
            finally
            {
                try
                {
                    Utilities.Log("Deleting Resource Group: " + rgName);
                    await resourceGroup.DeleteAsync(WaitUntil.Completed);
                    Utilities.Log("Deleted Resource Group: " + rgName);
                }
                catch (NullReferenceException)
                {
                    Utilities.Log("Did not create any resources in IAzure. No clean up is necessary");
                }
                catch (Exception g)
                {
                    Utilities.Log(g);
                }
            }
        }

        public static async Task Main(string[] args)
        {
            try
            {
                //=============================================================
                // Authenticate
                var clientId = Environment.GetEnvironmentVariable("CLIENT_ID");
                var clientSecret = Environment.GetEnvironmentVariable("CLIENT_SECRET");
                var tenantId = Environment.GetEnvironmentVariable("TENANT_ID");
                var subscription = Environment.GetEnvironmentVariable("SUBSCRIPTION_ID");
                ClientSecretCredential credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
                ArmClient client = new ArmClient(credential, subscription);

                // Print selected subscription
                Utilities.Log("Selected subscription: " + client.GetSubscriptions().Id);

                await RunSample(client);
            }
            catch (Exception ex)
            {
                Utilities.Log(ex);
            }
        }

        private static VirtualMachineResource PrepareSpecializedUnmanagedVirtualMachine(ResourceGroupResource resourceGroup)
        {
            var userName = Utilities.CreateUsername();
            var password = Utilities.CreatePassword();
            var linuxVmName1 = Utilities.CreateRandomName("vm" + "-");
            var publicIpDnsLabel = Utilities.CreateRandomName("pip" + "-");
            var pipName = Utilities.CreateRandomName("pip1");
            var networkName = Utilities.CreateRandomName("VirtualNetwork_");
            var linuxComputerName = Utilities.CreateRandomName("linuxComputer");
            var networkCollection = resourceGroup.GetVirtualNetworks();
            var networkData = new VirtualNetworkData()
            {
                AddressPrefixes =
                    {
                        "10.0.0.0/28"
                    }
            };
            var publicIpAddressCollection = resourceGroup.GetPublicIPAddresses();
            var publicIPAddressData = new PublicIPAddressData()
            {
                DnsSettings =
                            {
                                DomainNameLabel = publicIpDnsLabel
                            }
            };
            var publicIpAddressCreatable = (publicIpAddressCollection.CreateOrUpdate(Azure.WaitUntil.Completed, pipName, publicIPAddressData)).Value;
            var networkCreatable = networkCollection.CreateOrUpdate(Azure.WaitUntil.Completed, networkName, networkData).Value;
            var subnetName = Utilities.CreateRandomName("subnet_");
            var subnetData = new SubnetData()
            {
                ServiceEndpoints =
                    {
                        new ServiceEndpointProperties()
                        {
                            Service = "Microsoft.Storage"
                        }
                    },
                Name = subnetName,
                AddressPrefix = "10.0.0.0/28",
            };
            var subnetLRro = networkCreatable.GetSubnets().CreateOrUpdate(WaitUntil.Completed, subnetName, subnetData);
            var subnet = subnetLRro.Value;
            var networkInterfaceData = new NetworkInterfaceData()
            {
                Location = AzureLocation.EastUS,
                IPConfigurations =
                    {
                        new NetworkInterfaceIPConfigurationData()
                        {
                            Name = "internal",
                            Primary = true,
                            Subnet = new SubnetData
                            {
                                Name = subnetName,
                                Id = new ResourceIdentifier($"{networkCreatable.Data.Id}/subnets/{subnetName}")
                            },
                            PrivateIPAllocationMethod = NetworkIPAllocationMethod.Dynamic,
                            PublicIPAddress = publicIpAddressCreatable.Data,
                        }
                    }
            };
            var networkInterfaceName = Utilities.CreateRandomName("networkInterface");
            var nic = (resourceGroup.GetNetworkInterfaces().CreateOrUpdate(WaitUntil.Completed, networkInterfaceName, networkInterfaceData)).Value;
            var vmCollection = resourceGroup.GetVirtualMachines();
            var linuxVmdata = new VirtualMachineData(AzureLocation.EastUS)
            {
                HardwareProfile = new VirtualMachineHardwareProfile()
                {
                    VmSize = "Standard_D2a_v4"
                },
                OSProfile = new VirtualMachineOSProfile()
                {
                    AdminUsername = userName,
                    AdminPassword = password,
                    ComputerName = linuxComputerName,
                },
                NetworkProfile = new VirtualMachineNetworkProfile()
                {
                    NetworkInterfaces =
                        {
                            new VirtualMachineNetworkInterfaceReference()
                            {
                                Id = nic.Id,
                                Primary = true,
                            }
                        }
                },
                StorageProfile = new VirtualMachineStorageProfile()
                {
                    OSDisk = new VirtualMachineOSDisk(DiskCreateOptionType.FromImage)
                    {
                        OSType = SupportedOperatingSystemType.Linux,
                        Caching = CachingType.ReadWrite,
                        ManagedDisk = new VirtualMachineManagedDisk()
                        {
                            StorageAccountType = StorageAccountType.StandardLrs
                        }
                    },
                    DataDisks =
                    {
                        new VirtualMachineDataDisk(1, DiskCreateOptionType.FromImage)
                        {
                            Name = "disk-1",
                            DiskSizeGB = 100,
                        },
                        new VirtualMachineDataDisk(2, DiskCreateOptionType.FromImage)
                        {
                            Name = "disk-2",
                            DiskSizeGB = 50
                        },
                    },
                    ImageReference = new ImageReference()
                    {
                        Publisher = "Canonical",
                        Offer = "UbuntuServer",
                        Sku = "16.04-LTS",
                        Version = "latest",
                    },
                },
            };
            var virtualMachineResource = vmCollection.CreateOrUpdate(WaitUntil.Completed, linuxVmName1, linuxVmdata).Value;

            Utilities.Log("Deallocate VM: " + virtualMachineResource.Id);
            virtualMachineResource.Deallocate(WaitUntil.Completed);
            Utilities.Log("Deallocated VM: " + virtualMachineResource.Id + "; state = " + virtualMachineResource.Data.ProvisioningState);
            Utilities.Log("Generalize VM: " + virtualMachineResource.Id);
            virtualMachineResource.Generalize();
            Utilities.Log("Generalized VM: " + virtualMachineResource.Id);
            return virtualMachineResource;
        }

        private static VirtualMachineResource PrepareSpecializedManagedVirtualMachine(AzureLocation region, ResourceGroupResource resourceGroup)
        {
            var userName = Utilities.CreateUsername();
            var password = Utilities.CreatePassword();
            var linuxVmName1 = Utilities.CreateRandomName("vm" + "-");
            var publicIpDnsLabel = Utilities.CreateRandomName("pip" + "-");
            var pipName = Utilities.CreateRandomName("pip1");
            var networkName = Utilities.CreateRandomName("VirtualNetwork_");
            var linuxComputerName = Utilities.CreateRandomName("linuxComputer");
            var networkCollection = resourceGroup.GetVirtualNetworks();
            var networkData = new VirtualNetworkData()
            {
                AddressPrefixes =
                    {
                        "10.0.0.0/28"
                    }
            };
            var publicIpAddressCollection = resourceGroup.GetPublicIPAddresses();
            var publicIPAddressData = new PublicIPAddressData()
            {
                DnsSettings =
                            {
                                DomainNameLabel = publicIpDnsLabel
                            }
            };
            var publicIpAddressCreatable = (publicIpAddressCollection.CreateOrUpdate(Azure.WaitUntil.Completed, pipName, publicIPAddressData)).Value;
            var networkCreatable = networkCollection.CreateOrUpdate(Azure.WaitUntil.Completed, networkName, networkData).Value;
            var subnetName = Utilities.CreateRandomName("subnet_");
            var subnetData = new SubnetData()
            {
                ServiceEndpoints =
                    {
                        new ServiceEndpointProperties()
                        {
                            Service = "Microsoft.Storage"
                        }
                    },
                Name = subnetName,
                AddressPrefix = "10.0.0.0/28",
            };
            var subnetLRro = networkCreatable.GetSubnets().CreateOrUpdate(WaitUntil.Completed, subnetName, subnetData);
            var subnet = subnetLRro.Value;
            var networkInterfaceData = new NetworkInterfaceData()
            {
                Location = AzureLocation.EastUS,
                IPConfigurations =
                    {
                        new NetworkInterfaceIPConfigurationData()
                        {
                            Name = "internal",
                            Primary = true,
                            Subnet = new SubnetData
                            {
                                Name = subnetName,
                                Id = new ResourceIdentifier($"{networkCreatable.Data.Id}/subnets/{subnetName}")
                            },
                            PrivateIPAllocationMethod = NetworkIPAllocationMethod.Dynamic,
                            PublicIPAddress = publicIpAddressCreatable.Data,
                        }
                    }
            };
            var networkInterfaceName = Utilities.CreateRandomName("networkInterface");
            var nic = (resourceGroup.GetNetworkInterfaces().CreateOrUpdate(WaitUntil.Completed, networkInterfaceName, networkInterfaceData)).Value;
            var vmCollection = resourceGroup.GetVirtualMachines();
            var linuxVmdata = new VirtualMachineData(AzureLocation.EastUS)
            {
                HardwareProfile = new VirtualMachineHardwareProfile()
                {
                    VmSize = "Standard_D2a_v4"
                },
                OSProfile = new VirtualMachineOSProfile()
                {
                    AdminUsername = userName,
                    AdminPassword = password,
                    ComputerName = linuxComputerName,
                },
                NetworkProfile = new VirtualMachineNetworkProfile()
                {
                    NetworkInterfaces =
                        {
                            new VirtualMachineNetworkInterfaceReference()
                            {
                                Id = nic.Id,
                                Primary = true,
                            }
                        }
                },
                StorageProfile = new VirtualMachineStorageProfile()
                {
                    OSDisk = new VirtualMachineOSDisk(DiskCreateOptionType.FromImage)
                    {
                        OSType = SupportedOperatingSystemType.Linux,
                        Caching = CachingType.ReadWrite,
                        ManagedDisk = new VirtualMachineManagedDisk()
                        {
                            StorageAccountType = StorageAccountType.StandardLrs
                        }
                    },
                    DataDisks =
                    {
                        new VirtualMachineDataDisk(1, DiskCreateOptionType.FromImage)
                        {
                            Name = "disk-1",
                            DiskSizeGB = 100,
                        },
                        new VirtualMachineDataDisk(2, DiskCreateOptionType.FromImage)
                        {
                            Name = "disk-2",
                            DiskSizeGB = 200
                        },
                    },
                    ImageReference = new ImageReference()
                    {
                        Publisher = "Canonical",
                        Offer = "UbuntuServer",
                        Sku = "16.04-LTS",
                        Version = "latest",
                    },
                },
            };
            var virtualMachineResource = vmCollection.CreateOrUpdate(WaitUntil.Completed, linuxVmName1, linuxVmdata).Value;

            Utilities.Log("Deallocate VM: " + virtualMachineResource.Id);
            virtualMachineResource.Deallocate(WaitUntil.Completed);
            Utilities.Log("Deallocated VM: " + virtualMachineResource.Id + "; state = " + virtualMachineResource.Data.ProvisioningState);
            Utilities.Log("Generalize VM: " + virtualMachineResource.Id);
            virtualMachineResource.Generalize();
            Utilities.Log("Generalized VM: " + virtualMachineResource.Id);
            return virtualMachineResource;
        }
        
        private static VirtualNetworkResource PrepareNetwork(AzureLocation region, ResourceGroupResource resourceGroup)
        {
            var vnetName = Utilities.CreateRandomName("vnet");
            var virtualNetworkCollection = resourceGroup.GetVirtualNetworks();
            var data = new VirtualNetworkData()
            {
                Location = AzureLocation.EastUS,
                AddressPrefixes =
                    {
                        new string("172.16.0.0/16"),
                    },
                Subnets =
                {
                    new SubnetData()
                    {
                        Name = "subnet1",
                        AddressPrefix = "172.16.1.0/24"
                    }
                }
            };
            var virtualNetworkLro = virtualNetworkCollection.CreateOrUpdate(WaitUntil.Completed, vnetName, data);
            var virtualNetwork = virtualNetworkLro.Value;
            return virtualNetwork;
        }

        private static ILoadBalancer PrepareLoadBalancer(IAzure azure, Region region, string rgName)
        {
            var loadBalancerName1 = SdkContext.RandomResourceName("intlb" + "-", 18);
            var frontendName = loadBalancerName1 + "-FE1";
            var backendPoolName1 = loadBalancerName1 + "-BAP1";
            var backendPoolName2 = loadBalancerName1 + "-BAP2";
            var httpProbe = "httpProbe";
            var httpsProbe = "httpsProbe";
            var httpLoadBalancingRule = "httpRule";
            var httpsLoadBalancingRule = "httpsRule";
            var natPool50XXto22 = "natPool50XXto22";
            var natPool60XXto23 = "natPool60XXto23";
            var publicIpName = "pip-" + loadBalancerName1;

            var publicIpAddress = azure.PublicIPAddresses.Define(publicIpName)
                    .WithRegion(region)
                    .WithExistingResourceGroup(rgName)
                    .WithLeafDomainLabel(publicIpName)
                .Create();
            var loadBalancer = azure.LoadBalancers.Define(loadBalancerName1)
                    .WithRegion(region)
                    .WithExistingResourceGroup(rgName)
                    // Add two rules that uses above backend and probe
                    .DefineLoadBalancingRule(httpLoadBalancingRule)
                        .WithProtocol(TransportProtocol.Tcp)
                        .FromFrontend(frontendName)
                        .FromFrontendPort(80)
                        .ToBackend(backendPoolName1)
                        .WithProbe(httpProbe)
                        .Attach()
                    .DefineLoadBalancingRule(httpsLoadBalancingRule)
                        .WithProtocol(TransportProtocol.Tcp)
                        .FromFrontend(frontendName)
                        .FromFrontendPort(443)
                        .ToBackend(backendPoolName2)
                        .WithProbe(httpsProbe)
                        .Attach()
                    // Add nat pools to enable direct VM connectivity for
                    //  SSH to port 22 and TELNET to port 23
                    .DefineInboundNatPool(natPool50XXto22)
                        .WithProtocol(TransportProtocol.Tcp)
                        .FromFrontend(frontendName)
                        .FromFrontendPortRange(5000, 5099)
                        .ToBackendPort(22)
                        .Attach()
                    .DefineInboundNatPool(natPool60XXto23)
                        .WithProtocol(TransportProtocol.Tcp)
                        .FromFrontend(frontendName)
                        .FromFrontendPortRange(6000, 6099)
                        .ToBackendPort(23)
                        .Attach()
                    // Explicitly define the frontend
                    .DefinePublicFrontend(frontendName)
                        .WithExistingPublicIPAddress(publicIpAddress)
                        .Attach()
                    // Add two probes one per rule
                    .DefineHttpProbe(httpProbe)
                        .WithRequestPath("/")
                        .WithPort(80)
                        .Attach()
                    .DefineHttpProbe(httpsProbe)
                        .WithRequestPath("/")
                        .WithPort(443)
                        .Attach()
                    .Create();
            return loadBalancer;
        }
    }
}