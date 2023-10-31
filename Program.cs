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
            var vmssNetworkConfigurationName = Utilities.CreateRandomName("networkConfiguration");
            var ipConfigurationName = Utilities.CreateRandomName("ipconfigruation");
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
                var vmScaleSetVMCollection = resourceGroup.GetVirtualMachineScaleSets();
                var scaleSetData = new VirtualMachineScaleSetData(region)
                {
                    Sku = new ComputeSku()
                    {
                        Name = "VirtualMachineScaleSetSkuTypes.StandardD5v2",
                        Capacity = 3,
                    },
                    VirtualMachineProfile = new VirtualMachineScaleSetVmProfile()
                    {
                        OSProfile = new VirtualMachineScaleSetOSProfile()
                        {
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
                        StorageProfile = new VirtualMachineScaleSetStorageProfile()
                        {
                            DataDisks =
                            {
                                new VirtualMachineScaleSetDataDisk(1, DiskCreateOptionType.FromImage)
                                {
                                    DiskSizeGB = 100,
                                    Caching = CachingType.ReadWrite
                                },
                                new VirtualMachineScaleSetDataDisk(2, DiskCreateOptionType.FromImage)
                                {
                                    DiskSizeGB = 100,
                                    Caching = CachingType.ReadOnly
                                },
                                new VirtualMachineScaleSetDataDisk(3, DiskCreateOptionType.Attach)
                                {
                                    DiskSizeGB = 100,
                                },
                            },
                            ImageReference = new ImageReference()
                            {
                                Publisher = "Canonical",
                                Offer = "UbuntuServer",
                                Sku = "16.04-LTS",
                                Version = "latest"
                            }
                        },
                        NetworkProfile = new VirtualMachineScaleSetNetworkProfile()
                        {
                            NetworkInterfaceConfigurations =
                           {
                               new VirtualMachineScaleSetNetworkConfiguration(vmssNetworkConfigurationName)
                               {
                                   IPConfigurations =
                                   {
                                       new VirtualMachineScaleSetIPConfiguration(ipConfigurationName)
                                       {
                                           LoadBalancerBackendAddressPools =
                                           {
                                               new Azure.ResourceManager.Resources.Models.WritableSubResource()
                                               {
                                                   Id = PrepareLoadBalancer(region, resourceGroup).Id
                                               }
                                           },
                                           SubnetId = PrepareNetwork(region, resourceGroup).Id,
                                       }
                                   }
                               }
                           }
                        },
                    },

                };
                var vmScaleSet = (await vmScaleSetVMCollection.CreateOrUpdateAsync(WaitUntil.Completed, vmScaleSetName, scaleSetData)).Value;

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
                var linuxVM4 = vmCollection.CreateOrUpdate(WaitUntil.Completed, linuxVmName4, linuxVmdata4).Value;

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
                var newOSDisk = new ManagedDiskData(region)
                {
                    CreationData = new DiskCreationData(DiskCreateOption.Copy)
                    {
                        SourceResourceId = new ResourceIdentifier(osSnapshot.Id),
                    },
                    DiskSizeGB = 100,
                };

                Utilities.Log("Created managed OS disk [from snapshot]");

                Utilities.Log("Creating managed data snapshot [from managed data disk]");

                // Create a managed snapshot for a data disk
                var managedDataDiskSnapshotName = Utilities.CreateRandomName("dsk" + "-");
                var dataSnapshot = new SnapshotData(region)
                {
                    Sku = new SnapshotSku()
                    {
                        Name = "DiskSkuTypes.StandardLRS"
                    },
                    CreationData = new DiskCreationData(DiskCreateOption.Copy)
                    {
                        SourceResourceId = new ResourceIdentifier(dataDisks[0].Name)
                    }
                };

                Utilities.Log("Created managed data snapshot [from managed data disk]");

                Utilities.Log("Creating managed data disk [from managed snapshot]");

                // Create a managed disk from the managed snapshot for the data disk
                var managedNewDataDiskName = Utilities.CreateRandomName("dsk" + "-");
                var newDataDiskData = new ManagedDiskData(region)
                {
                    CreationData = new DiskCreationData(DiskCreateOption.Copy)
                    {
                        SourceResourceId = new ResourceIdentifier(dataSnapshot.Id)
                    }
                };
                var newDataDisk = (await diskCollection.CreateOrUpdateAsync(WaitUntil.Completed, managedNewDataDiskName, newDataDiskData)).Value;

                Utilities.Log("Created managed data disk [from managed snapshot]");

                Utilities.Log("Creating VM [with specialized OS managed disk]");

                var linuxVm6Name = Utilities.CreateRandomName("vm" + "-");

                Utilities.Log("Creating VM [by attaching un-managed disk]");
                var linuxVmdata6 = new VirtualMachineData(AzureLocation.EastUS)
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
                            OSType = SupportedOperatingSystemType.Linux,
                            ManagedDisk = new VirtualMachineManagedDisk()
                            {
                                DiskEncryptionSetId = newOSDisk.Id,
                            }
                        },
                        DataDisks =
                        {
                            new VirtualMachineDataDisk(1, DiskCreateOptionType.Attach)
                            {
                                ManagedDisk = new VirtualMachineManagedDisk()
                                {
                                   DiskEncryptionSetId = newDataDisk.Id
                                }
                            }
                        }
                    },
                };
                var linuxVM6 = vmCollection.CreateOrUpdate(WaitUntil.Completed, linuxVm6Name, linuxVmdata6).Value;

                Utilities.Log("Created VM [with specialized OS managed disk]");

                // ::== Migrate a VM to managed disks with a single reboot

                Utilities.Log("Creating VM [with un-managed disk for migration]");

                var linuxVM7Name = Utilities.CreateRandomName("vm" + "-");
                var linuxVmdata7 = new VirtualMachineData(AzureLocation.EastUS)
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
                var linuxVM7 = vmCollection.CreateOrUpdate(WaitUntil.Completed, linuxVM7Name, linuxVmdata7).Value;

                Utilities.Log("Created VM [with un-managed disk for migration]");

                Utilities.Log("De-allocating VM :" + linuxVM7.Id);

                await linuxVM7.DeallocateAsync(WaitUntil.Completed);

                Utilities.Log("De-allocated VM :" + linuxVM7.Id);

                Utilities.Log("Migrating VM");

                await linuxVM7.ConvertToManagedDisksAsync(WaitUntil.Completed);

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

        private static LoadBalancerResource PrepareLoadBalancer(AzureLocation region, ResourceGroupResource resourceGroup)
        {
            var loadBalancerName1 = Utilities.CreateRandomName("intlb" + "-");
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

            var loadBalancerCollection = resourceGroup.GetLoadBalancers();
            var loadBalancerData = new LoadBalancerData()
            {
                Location = region,
                LoadBalancingRules =
                    {
                        new LoadBalancingRuleData()
                        {
                            Name = httpLoadBalancingRule,
                            Protocol = LoadBalancingTransportProtocol.Tcp,
                            FrontendPort = 80,
                            ProbeId = new ResourceIdentifier(httpProbe),
                            BackendAddressPools =
                            {
                                new Azure.ResourceManager.Resources.Models.WritableSubResource()
                                {
                                    Id = new ResourceIdentifier(backendPoolName1)
                                }
                            }
                        },
                        new LoadBalancingRuleData()
                        {
                            Name= httpsLoadBalancingRule,
                            Protocol= LoadBalancingTransportProtocol.Tcp,
                            BackendPort = 443,
                            ProbeId = new ResourceIdentifier(httpsProbe),
                            BackendAddressPools =
                            {
                                new Azure.ResourceManager.Resources.Models.WritableSubResource()
                                {
                                    Id = new ResourceIdentifier(backendPoolName2)
                                }
                            }
                        }
                    },
                // Add nat pools to enable direct VM connectivity for
                //  SSH to port 22 and TELNET to port 23
                InboundNatRules =
                    {
                        new InboundNatRuleData()
                        {
                            Name = natPool50XXto22,
                            Protocol= LoadBalancingTransportProtocol.Tcp,
                            BackendPort = 22,
                            FrontendPortRangeStart = 5000,
                            FrontendPortRangeEnd = 5099,
                        },
                        new InboundNatRuleData()
                        {
                            Name = natPool60XXto23,
                            Protocol= LoadBalancingTransportProtocol.Tcp,
                            BackendPort = 23,
                            FrontendPortRangeStart = 6000,
                            FrontendPortRangeEnd = 6099,
                        }
                    },
                // Add two probes one per rule
                Probes =
                    {
                        new ProbeData()
                        {
                            RequestPath = "/",
                            Port = 80,
                        },
                        new ProbeData()
                        {
                            RequestPath = "/",
                            Port = 443,
                        }
                    }
            };
            var loadBalancer = loadBalancerCollection.CreateOrUpdate(Azure.WaitUntil.Completed, loadBalancerName1, loadBalancerData).Value;
            return loadBalancer;
        }
    }
}