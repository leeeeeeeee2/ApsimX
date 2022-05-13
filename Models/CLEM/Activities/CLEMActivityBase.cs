using Models.Core;
using Models.CLEM.Interfaces;
using Models.CLEM.Resources;
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Models.CLEM.Groupings;
using Models.Core.Attributes;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using APSIM.Shared.Utilities;

namespace Models.CLEM.Activities
{
    ///<summary>
    /// CLEM Activity base model
    ///</summary> 
    [Serializable]
    [ViewName("UserInterface.Views.PropertyView")]
    [PresenterName("UserInterface.Presenters.PropertyPresenter")]
    [Description("This is the CLEM Activity Base Class and should not be used directly.")]
    [Version(1, 0, 1, "")]
    public abstract class CLEMActivityBase: CLEMModel, IReportPartialResourceAction
    {
        /// <summary>
        /// A protected link to the CLEM resource holder
        /// </summary>
        [Link(ByName = true)]
        protected ResourcesHolder Resources = null;

        /// <summary>
        /// Link to Activity holder
        /// </summary>
        [Link]
        [NonSerialized]
        public ActivitiesHolder ActivitiesHolder = null;

        private bool enabled = true;
        private ZoneCLEM parentZone = null;
        private Dictionary<string, object> companionModelsPresent = new Dictionary<string, object>();
        private protected Dictionary<(string type, string identifier, string unit), double?> valuesForCompanionModels = new Dictionary<(string type, string identifier, string unit), double?>();
        private Dictionary<string, LabelsForCompanionModels> companionModelLabels = new Dictionary<string, LabelsForCompanionModels>();
        private List<string> statusMessageList = new List<string>();

        /// <summary>
        /// Label to assign each transaction created by this activity in ledgers
        /// </summary>
        [Description("Category for transactions")]
        [Required(AllowEmptyStrings = false, ErrorMessage = "Category for transactions required")]
        [Models.Core.Display(Order = 500)]
        virtual public string TransactionCategory { get; set; }

        /// <inheritdoc/>
        [Description("Insufficient resources available action")]
        [Models.Core.Display(Order = 1000)]
        public OnPartialResourcesAvailableActionTypes OnPartialResourcesAvailableAction { get; set; }

        ///<inheritdoc/>
        public bool AllowsPartialResourcesAvailable { get { return OnPartialResourcesAvailableAction == OnPartialResourcesAvailableActionTypes.UseAvailableResources || OnPartialResourcesAvailableAction == OnPartialResourcesAvailableActionTypes.UseAvailableWithImplications; } }

        /// <summary>
        /// Current list of resources requested by this activity
        /// </summary>
        [JsonIgnore]
        public List<ResourceRequest> ResourceRequestList { get; set; }

        /// <inheritdoc/>
        [JsonIgnore]
        public ActivityStatus Status { get; set; }

        /// <inheritdoc/>
        [JsonIgnore]
        public string StatusMessage { get { return string.Join(Environment.NewLine, statusMessageList); }}

        /// <summary>
        /// Resource allocation style
        /// </summary>
        [JsonIgnore]
        public ResourceAllocationStyle AllocationStyle { get; set; }

        /// <summary>
        /// Current status of this activity
        /// </summary>
        [JsonIgnore]
        public bool ActivityEnabled
        {
            get
            {
                return enabled;
            }
            set
            {
                if(value!=enabled)
                    foreach (var child in FindAllChildren<CLEMActivityBase>())
                        child.ActivityEnabled = value;
                    enabled = value;
            }
        }

        /// <summary>
        /// Multiplier for farms in this zone
        /// </summary>
        public double FarmMultiplier 
        {
            get
            {
                if(parentZone is null)
                    parentZone = FindAncestor<ZoneCLEM>();

                if(parentZone is null)
                    return 1;
                else
                    return parentZone.FarmMultiplier;
            }
        }

        /// <summary>
        /// Property to check if timing of this activity is ok based on child and parent ActivityTimers in UI tree
        /// </summary>
        /// <returns>T/F</returns>
        public virtual new bool TimingOK
        {
            get
            {
                // use timing to not perform activity based on Enabled state
                if (!ActivityEnabled)
                    return false;

                // sum all where true=0 and false=1 so that all must be zero to get a sum total of zero or there are no timers
                int result = 0;
                IModel current = this as IModel;
                while (current.GetType() != typeof(ZoneCLEM) & current.GetType() != typeof(Market))
                {
                    if(current is CLEMModel)
                        result += (current as CLEMModel).ActivityTimers.Sum(a => a.ActivityDue ? 0 : 1);
                    current = current.Parent as IModel;
                }
                return (result == 0);
            }
        }

        /// <summary>
        /// Method to check if timing of this activity is ok based on child and parent ActivityTimers in UI tree and a specified date
        /// </summary>
        /// <returns>T/F</returns>
        public bool TimingCheck(DateTime date)
        {
            // use timing to not perform activity based on Enabled state
            if (!ActivityEnabled)
                return false;

            // sum all where true=0 and false=1 so that all must be zero to get a sum total of zero or there are no timers
            int result = 0;
            IModel current = this as IModel;
            while (current.GetType() != typeof(ZoneCLEM))
            {
                if (current is CLEMModel)
                    result += (current as CLEMModel).ActivityTimers.Sum(a => a.Check(date) ? 0 : 1);
                current = current.Parent as IModel;
            }
            return (result == 0);
        }

        /// <summary>
        /// Property to check if timing of this activity is ok based on child and parent ActivityTimers in UI tree
        /// </summary>
        /// <returns>T/F</returns>
        public bool TimingExists
        {
            get
            {
                // sum all where true=0 and false=1 so that all must be zero to get a sum total of zero or there are no timers
                int result = 0;
                IModel current = this as IModel;
                while (current.GetType() != typeof(ZoneCLEM))
                {
                    if (current is CLEMModel)
                        result += (current as CLEMModel).ActivityTimers.Count();
                    current = current.Parent as IModel;
                }
                return (result != 0);
            }
        }

        /// <summary>
        /// A method to allow the resource holder to be set when [Link] not possible for dynamically created model
        /// </summary>
        /// <param name="resourceHolder">The resource holder to provide</param>
        public void SetLinkedModels(ResourcesHolder resourceHolder)
        {
            Resources = resourceHolder;
        }

        /// <summary>
        /// Report error or set status to partial if shortfall found otherwise set to success if notneeded
        /// </summary>
        public void SetStatusSuccessOrPartial(bool shortfallOccurred = false)
        {
            if (Status != ActivityStatus.Warning)
            {
                if (shortfallOccurred)
                {
                    if (OnPartialResourcesAvailableAction == OnPartialResourcesAvailableActionTypes.ReportErrorAndStop)
                        throw new ApsimXException(this, $"Shortfall of resources occurred in [a={NameWithParent}]{Environment.NewLine}Ensure resources are available, enable transmutation, or set OnPartialResourcesAvailableAction to [UseResourcesAvailable]");
                    else
                    {
                        if(Status != ActivityStatus.Skipped)
                            this.Status = ActivityStatus.Partial;
                    }
                }
                else
                {
                    if (Status == ActivityStatus.NotNeeded)
                        Status = ActivityStatus.Success;
                }
            }
        }

        #region Companion child model handling

        /// <summary>
        /// A method to return the list of labels provided by the parent activity for the given companion child model type
        /// </summary>
        /// <param name="labelType">The type of labels to provide</param>
        /// <typeparam name="T">Companion child model type</typeparam>
        /// <returns>List of labels for the selected style</returns>
        public List<string> CompanionModelLabels<T>(CompanionModelLabelType labelType) where T : IActivityCompanionModel
        {
            if (this is IHandlesActivityCompanionModels)
            {
                LabelsForCompanionModels labels;
                if (companionModelLabels.ContainsKey(typeof(T).Name))
                {
                    labels = companionModelLabels[typeof(T).Name];
                }
                else
                {
                    labels = DefineCompanionModelLabels(typeof(T).Name);
                    companionModelLabels.Add(typeof(T).Name, labels);
                }
                switch (labelType)
                {
                    case CompanionModelLabelType.Identifiers:
                        return labels.Identifiers;
                    case CompanionModelLabelType.Measure:
                        return labels.Measures;
                    default:
                        break;
                }
                return new List<string>();
            }
            else
                throw new NotImplementedException($"[a={NameWithParent}] does not support Companion models to perform custom tasks with resource provision.");
        }

        /// <summary>
        /// A method to get a list of activity specified labels for a generic type T 
        /// </summary>
        /// <param name="type">The type of child model</param>
        /// <returns>A LabelsForCompanionModels containing all labels</returns>
        public virtual LabelsForCompanionModels DefineCompanionModelLabels(string type)
        {
            return new LabelsForCompanionModels();
        }

        /// <summary>An event handler to allow us to make checks after resources and activities initialised.</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("StartOfSimulation")]
        protected virtual void OnStartOfSimulationGetCompanionModels(object sender, EventArgs e)
        {
            // if this activity supports companion child models for controlling resource requirements
            if (this is IHandlesActivityCompanionModels)
            {
                // for each ICompanion type in direct children 
                foreach (Type componentType in FindAllChildren<IActivityCompanionModel>().Select(a => a.GetType()).Distinct())
                {
                    switch (componentType.Name)
                    {
                        case "RuminantGroup":
                        case "RuminantGroupLinked":
                            companionModelsPresent.Add("RuminantGroup", LocateCompanionModels<RuminantGroup>());
                            break;
                        case "RuminantFeedGroup":
                        case "RuminantFeedGroupMonthly":
                            companionModelsPresent.Add(componentType.Name, LocateCompanionModels<RuminantFeedGroup>());
                            break;
                        case "LabourRequirement":
                            companionModelsPresent.Add(componentType.Name, LocateCompanionModels<LabourRequirement>());
                            break;
                        case "ActivityFee":
                            companionModelsPresent.Add(componentType.Name, LocateCompanionModels<ActivityFee>());
                            break;
                        case "RuminantTrucking":
                            companionModelsPresent.Add(componentType.Name, LocateCompanionModels<RuminantTrucking>());
                            break;
                        case "GreenhouseGasActivityEmission":
                            companionModelsPresent.Add(componentType.Name, LocateCompanionModels<GreenhouseGasActivityEmission>());
                            break;
                        case "Relationship":
                            companionModelsPresent.Add(componentType.Name, LocateCompanionModels<Relationship>());
                            break;
                        default:
                            throw new NotSupportedException($"{componentType.Name} not currently supported as activity companion component");
                    }
                } 
            }
        }

        /// <summary>
        /// Get the IEnumerable(T) of all activity specified companion models by type and identifer
        /// </summary>
        /// <typeparam name="T">The companion model type</typeparam>
        /// <param name="identifier">Identifer label</param>
        /// <param name="mustBeProvidedByUser">Determines if the parent requesting assumes the user will provide an instance of this child</param>
        /// <param name="addNewIfEmpty">Create IENumuerable with a new() instance of T</param>
        /// <returns>IEnumerable of T found</returns>
        protected private IEnumerable<T> GetCompanionModelsByIdentifier<T>(bool mustBeProvidedByUser, bool addNewIfEmpty, string identifier = "") where T : IActivityCompanionModel, new()
        {
            if (companionModelsPresent.ContainsKey(typeof(T).Name))
            {
                if (companionModelsPresent[typeof(T).Name] is Dictionary<string, IEnumerable<T>> foundTypeDictionary)
                {
                    if (foundTypeDictionary.ContainsKey(identifier))
                    {
                        return foundTypeDictionary[identifier];
                    }
                    else
                    {
                        if(CompanionModelLabels<T>(CompanionModelLabelType.Identifiers).Contains(identifier) == false)
                            throw new NotSupportedException($"[{GetType().Name}] does not support the identifier [{identifier}]{Environment.NewLine}Internal error during request for companion child models: request support from developers.");
                    }
                }
            }
            if (mustBeProvidedByUser)
            {
                string warn = $"[a={NameWithParent}] requires at least one [{typeof(T).Name}] as a companion component {((identifier == "") ? "with the appropriate identifier" : $"with the Identifier set as [{identifier}]")} to specify individuals";
                Warnings.CheckAndWrite(warn, Summary, this, MessageType.Error);
            }
            else
            {
                if (addNewIfEmpty)
                    return new List<T>() { new T() };
            }
            return null;
        }

        /// <summary>
        /// Create a dictionary of groups of components by identifier provided by the parent model
        /// </summary>
        /// <typeparam name="T">Type of component to consider</typeparam>
        /// <returns></returns>
        protected private Dictionary<string, IEnumerable<T>> LocateCompanionModels<T>() where T : IActivityCompanionModel, new()
        {
            Dictionary<string, IEnumerable<T>> filters = new Dictionary<string, IEnumerable<T>>();

            var ids = CompanionModelLabels<T>(CompanionModelLabelType.Identifiers);
            if (ids is null)
                throw new Exception($"Identifiers have not been correctly configured for companion models of type [{typeof(T).Name}] of [{GetType().Name}]{Environment.NewLine}Invalid setup of [{GetType().Name}].LocateCompanionModels<T>(). Contact developers for assistance");

            if(ids.Any() == false)
                ids.Add("");
    
            foreach (var id in ids)
            {
                var iChildren = FindAllChildren<T>().Where(a => (a.Identifier??"") == id && a.Enabled);
                if (iChildren.Any())
                {
                    filters.Add(id, iChildren);
                    // if this type provides units for use by children add them
                    bool unitsProvided = CompanionModelLabels<T>(CompanionModelLabelType.Measure).Any();
                    if (unitsProvided)
                    {
                        foreach (var item in iChildren)
                        {
                            string unitsLabel = (unitsProvided ? item.Measure : "");
                            if (!valuesForCompanionModels.ContainsKey((typeof(T).Name, id, unitsLabel)))
                                valuesForCompanionModels.Add((typeof(T).Name, id, unitsLabel), 0);
                        } 
                    }
                }
            }
            return filters;
        }

        /// <summary>
        /// Return a error message string for Unknown identifier
        /// </summary>
        /// <param name="model">Model throwing the error</param>
        /// <param name="companionLabels">The details of labels saught</param>
        /// <returns>Formatted string for exception</returns>
        public static string UnknownCompanionModelErrorText(CLEMActivityBase model, (string type, string identifier, string unit) companionLabels)
        {
            return $"Type [{companionLabels.type}] is not supported by {model.GetType().Name}: [a={model.NameWithParent}]";
        }

        /// <summary>
        /// Return a error message string for Unknown identifier
        /// </summary>
        /// <param name="model">Model throwing the error</param>
        /// <param name="companionLabels">The details of labels saught</param>
        /// <returns>Formatted string for exception</returns>
        public static string UnknownIdentifierErrorText(CLEMActivityBase model, (string type, string identifier, string unit) companionLabels)
        {
            return $"Unknown identifier [{companionLabels.identifier}] used for [{companionLabels.type}] in [{model.GetType().Name}]: [{model.NameWithParent}]";
        }

        /// <summary>
        /// Return a error message string for Unknown identifier
        /// </summary>
        /// <param name="model">Model throwing the error</param>
        /// <param name="companionLabels">The details of labels saught</param>
        /// <returns>Formatted string for exception</returns>
        public static string UnknownUnitsErrorText(CLEMActivityBase model, (string type, string identifier, string unit) companionLabels)
        {
            return $"Unknown or invalid units [{((companionLabels.unit == "") ? "Blank" : companionLabels.unit)}] specified by companion component [{companionLabels.type}] {(((companionLabels.identifier ?? "")!="")?$"with the identifier [{companionLabels.identifier}]":"")} in [{model.GetType().Name}]: [a={model.NameWithParent}]";
        }

        #endregion

        /// <summary>A method to arrange clearing status on CLEMStartOfTimeStep event</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("CLEMStartOfTimeStep")]
        protected virtual void ResetActivityStatus(object sender, EventArgs e)
        {
            // clear Resources Required list
            ResourceRequestList = new List<ResourceRequest>();
            foreach (var key in valuesForCompanionModels.Keys.ToList())
            {
                valuesForCompanionModels[key] = null;
            }
            Status = ActivityStatus.Ignored;
            statusMessageList.Clear();
        }

        /// <summary>
        /// Add a new message to the list of current status messages
        /// </summary>
        /// <param name="message"></param>
        public void AddStatusMessage(string message)
        {
            statusMessageList.Add(message);
        }

        /// <summary>
        /// Protected method to cascade calls for activities performed for all dynamically created activities
        /// </summary>
        public void ReportActivityStatus()
        {
            this.TriggerOnActivityPerformed();

            // report all timers that were due this time step
            foreach (IActivityTimer timer in this.FindAllChildren<IActivityTimer>())
            {
                if (timer.ActivityDue)
                {
                    // report activity performed.
                    ActivitiesHolder?.ReportActivityPerformed(new ActivityPerformedEventArgs
                    {
                        Name = (timer as IModel).Name,
                        Status = ActivityStatus.Timer,
                        Id = (timer as CLEMModel).UniqueID.ToString(),
                    });
                }
            }
            // call activity performed for all children of type CLEMActivityBase
            foreach (CLEMActivityBase activity in FindAllChildren<CLEMActivityBase>())
                activity.ReportActivityStatus();
        }

        /// <summary>A method to arrange the activity to be performed on the specified clock event</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("CLEMGetResourcesRequired")]
        protected virtual void OnGetResourcesPerformActivity(object sender, EventArgs e)
        {
            if (AllocationStyle != ResourceAllocationStyle.Manual)
            {
                ManageActivityResourcesAndTasks();
            }
        }

        /// <summary>
        /// Mathod for parent to call this activity to run and manage transactions
        /// </summary>
        public void ManuallyGetResourcesPerformActivity()
        {
            ManageActivityResourcesAndTasks();
        }

        /// <summary>
        /// Return the current metric value from the parent for a specifie companion model
        /// </summary>
        /// <param name="companionModel">Reference to companion model</param>
        /// <returns>Current metric for the child</returns>
        public double ValueForCompanionModel(IActivityCompanionModel companionModel)
        {
            if(!valuesForCompanionModels.ContainsKey((companionModel.GetType().Name, companionModel.Identifier ?? "", companionModel.Measure ?? "")))
                throw new ApsimXException(this, $"Units for [{companionModel.GetType().Name}]-[{companionModel.Identifier ?? "BLANK"}]-[{companionModel.Measure ?? "BLANK"}] have not been calculated by [a={NameWithParent}] before this request.{Environment.NewLine}Code issue. See Developers");
            var unitsProvided = valuesForCompanionModels[(companionModel.GetType().Name, companionModel.Identifier ?? "", companionModel.Measure ?? "")];
            if (unitsProvided is null)
                throw new ApsimXException(this, $"Units for [{companionModel.GetType().Name}]-[{companionModel.Identifier ?? "BLANK"}]-[{companionModel.Measure ?? "BLANK"}] have not been calculated by [a={NameWithParent}] before this request.{Environment.NewLine}Code issue. See Developers");
            return unitsProvided.Value;
        }

        /// <summary>
        /// The main method to manage an activity based on resources available 
        /// </summary>
        protected virtual void ManageActivityResourcesAndTasks(string identifier = "")
        {
            if (Enabled)
            {
                if (TimingOK)
                {
                    // get ready for time step
                    PrepareForTimestep();

                    // allow all companion models to prepare after initial parent info calculated
                    if (this is IHandlesActivityCompanionModels)
                    {
                        // get all companion models except filter groups
                        foreach (IActivityCompanionModel companionChild in FindAllChildren<IActivityCompanionModel>().Where(a => identifier!=""?(a.Identifier??"") == identifier:true))
                            companionChild.PrepareForTimestep();
                    }

                    // add resources needed based on method supplied by activity
                    // set the metric values for identifiable children as they will follow in next loop
                    var requests = RequestResourcesForTimestep();
                    if (requests != null)
                        ResourceRequestList.AddRange(requests);

                    // get all companion related expense requests
                    if (this is IHandlesActivityCompanionModels)
                    {
                        // get all companion models except filter groups
                        foreach (IActivityCompanionModel companionChild in FindAllChildren<IActivityCompanionModel>().Where(a => identifier != "" ? (a.Identifier ?? "") == identifier : true))
                        {
                            if (valuesForCompanionModels.Any() && valuesForCompanionModels.Where(a => a.Key.type == companionChild.GetType().Name).Any())
                            {
                                var unitsProvided = ValueForCompanionModel(companionChild);
                                if (MathUtilities.IsPositive(unitsProvided))
                                {
                                    foreach (ResourceRequest request in companionChild.RequestResourcesForTimestep(unitsProvided))
                                    {
                                        if(request.ActivityModel is null)
                                            request.ActivityModel = this;
                                        request.CompanionModelDetails = (companionChild.GetType().Name, companionChild.Identifier, companionChild.Measure);
                                        ResourceRequestList.Add(request);
                                    }
                                }
                            }
                        }
                    }

                    // check availability and ok to proceed 
                    CheckResources(ResourceRequestList, Guid.NewGuid());

                    // adjust if needed using method supplied by activity
                    AdjustResourcesForTimestep();

                    if (ReportShortfalls(ResourceRequestList, Guid.NewGuid()) == false)
                    {
                        // take resources
                        // if no resources required perform Activity if code is present.
                        // if resources are returned (all available or UseResourcesAvailable action) perform Activity
                        if (TakeResources(ResourceRequestList, false) || (ResourceRequestList.Count == 0))
                        {
                            PerformTasksForTimestep(); //based on method supplied by activity

                            // for all companion models to generate create resources where needed
                            if (this is IHandlesActivityCompanionModels)
                            {
                                // get all companion models except filter groups
                                foreach (IActivityCompanionModel companionChild in FindAllChildren<IActivityCompanionModel>().Where(a => identifier != "" ? (a.Identifier ?? "") == identifier : true))
                                {
                                    if (valuesForCompanionModels.Any() && valuesForCompanionModels.Where(a => a.Key.type == companionChild.GetType().Name).Any())
                                    {
                                        var unitsProvided = ValueForCompanionModel(companionChild);
                                        // negative unit value (-99999) means the units were ok, but the model has alerted us to a problem that should eb reported as an error.
                                        if (MathUtilities.IsNegative(unitsProvided))
                                        {
                                            if (companionChild is CLEMActivityBase)
                                                (companionChild as CLEMActivityBase).Status = ActivityStatus.Warning;
                                        }
                                        else
                                        {
                                            if((companionChild as CLEMActivityBase).Status != ActivityStatus.Skipped)
                                                companionChild.PerformTasksForTimestep(unitsProvided);
                                        }
                                    }
                                }
                            }

                        }
                    }
                    return;
                }
            }
            else
            {
                Status = ActivityStatus.Ignored;
            }
        }

        /// <summary>
        /// Determine the min proportion shortfall for the current resource requests
        /// Considers those coming from the companion models and this activity
        /// </summary>
        /// <param name="reduceAllIdentifableShortfalls"></param>
        /// <returns>Minimum proportion found</returns>
        public IEnumerable<ResourceRequest> MinimumShortfallProportion(bool reduceAllIdentifableShortfalls = true)
        {
            double min = 1;
            if (ResourceRequestList != null && ResourceRequestList.Any())
            {
                // does shortfall work by resource?
                var shortfallRequests =  ResourceRequestList.Where(a => MathUtilities.IsNegative(a.Available - a.Required) && (a.ActivityModel as IReportPartialResourceAction).OnPartialResourcesAvailableAction == OnPartialResourcesAvailableActionTypes.UseAvailableWithImplications);
                if (shortfallRequests.Any())
                {
                    ResourceRequest minRequest = shortfallRequests.OrderBy(a => a.Available / a.Required).FirstOrDefault();
                    min = minRequest.Available / minRequest.Required;
                    foreach (var request in ResourceRequestList.Where(a => a != minRequest && MathUtilities.IsGreaterThan(a.Available/ a.Required, min)))
                        request.Required *= min;

                    // recalculate shortfalls in case any reduction in required removed request from shortfalls
                    return shortfallRequests.ToList();
                } 
            }
            return new List<ResourceRequest>();
        }

        /// <summary>A method to arrange the activity to be performed on the specified clock event</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("CLEMInitialiseResource")]
        protected virtual void OnValidateIdenfiableChildrenIdentifiersAndUnits(object sender, EventArgs e)
        {
            if (this is IHandlesActivityCompanionModels)
            {
                foreach (var iChild in FindAllChildren<IActivityCompanionModel>())
                {
                    // standardise the type if needed
                    string iChildType = (iChild is RuminantGroupLinked) ? "RuminantGroup" : iChild.GetType().Name;

                    var identifiers = DefineCompanionModelLabels(iChildType).Identifiers;

                    // tests for invalid identifier
                    bool test = ((iChild.Identifier ?? "") == "") == identifiers.Any();
                    bool test2 = identifiers.Any() && ((iChild.Identifier ?? "") != "") && !identifiers.Contains(iChild.Identifier ?? "");

                    if (test | test2)
                    {
                        string warn = $"The identifier [{(((iChild.Identifier??"") == "") ? "BLANK" : iChild.Identifier)}] specified in [{iChild.Name}] is not valid for the parent activity [a={NameWithParent}].{Environment.NewLine}Select an option from the list. If only the invalid value is displayed, edit the simulation file or delete and replace the component.";
                        Warnings.CheckAndWrite(warn, Summary, this, MessageType.Error);
                    }

                    var units = DefineCompanionModelLabels(iChildType).Measures;
                    test = ((iChild.Measure ?? "") == "") == units.Any();
                    test2 = units.Any() && ((iChild.Measure ?? "") != "") && !units.Contains(iChild.Measure ?? "");
                    if (test | test2)
                    {
                        string warn = $"The units [{(((iChild.Measure ?? "") == "") ? "BLANK" : iChild.Measure)}] specified in [{iChild.GetType().Name}]:[{iChild.Name}] are not valid for the parent activity [{GetType().Name}]:[a={NameWithParent}].{Environment.NewLine}Select an option from the list. If only the invalid value is displayed, edit the simulation file or delete and replace the component.";
                        Warnings.CheckAndWrite(warn, Summary, this, MessageType.Error);
                    }

                }
            }
        }

        /// <summary>
        /// Method to prepare the activitity for the time step 
        /// Functionality provided in derived classes
        /// </summary>
        public virtual void PrepareForTimestep()
        {
            Status = ActivityStatus.NotNeeded;
            return;
        }

        /// <summary>
        /// Method to determine the list of resources and amounts needed. 
        /// Functionality provided in derived classes
        /// </summary>
        public virtual List<ResourceRequest> RequestResourcesForTimestep(double argument = 0)
        {
            return null;
        }

        /// <summary>
        /// Method to adjust activities needed based on shortfalls before they are taken from resource pools. 
        /// Functionality provided in derived classes
        /// </summary>
        protected virtual void AdjustResourcesForTimestep()
        {
            IEnumerable<ResourceRequest> shortfalls = MinimumShortfallProportion();
            if (shortfalls.Any())
            {
                string warn = $"[a={GetType().Name}] activity [a={NameWithParent}] does not support resource shortfalls influencing the activity outcomes.{Environment.NewLine}Companion components with ShortfallAffectsActivity [true] will only apply [UseResourceAvailable] action.";
                Warnings.CheckAndWrite(warn, Summary, this, MessageType.Warning);
            }
            return;
        }

        /// <summary>
        /// Method to perform activity tasks if expected as soon as resources are available
        /// Functionality provided in derived classes
        /// </summary>
        public virtual void PerformTasksForTimestep(double argument = 0)
        {
            return;
        }

        ///// <summary>
        ///// Method to provide the proportional limit based on labour shortfall
        ///// A proportion less than 1 will only be returned if LabourShortfallAffectsActivity is true in the LabourRequirement
        ///// </summary>
        ///// <returns></returns>
        //public double LabourLimitProportion
        //{
        //    get
        //    {
        //        double proportion = 1.0;
        //        if (ResourceRequestList == null)
        //            return proportion;

        //        double totalNeeded = ResourceRequestList.Where(a => a.ResourceType == typeof(LabourType)).Sum(a => a.Required);

        //        foreach (ResourceRequest item in ResourceRequestList.Where(a => a.ResourceType == typeof(LabourType)))
        //            if (item.FilterDetails != null && ((item.FilterDetails.First() as LabourGroup).Parent as LabourRequirement).OnPartialResourcesAvailableAction == OnPartialResourcesAvailableActionTypes.UseAvailableWithImplications)
        //                proportion *= item.Provided / item.Required;
        //        return proportion;
        //    }
        //}

        ///// <summary>
        ///// Method to provide the proportional limit based on specified resource type
        ///// A proportion less than 1 will only be returned if LabourShortfallAffectsActivity is true in the LabourRequirement
        ///// </summary>
        ///// <returns></returns>
        //public double LimitProportion(Type resourceType)
        //{
        //    double proportion = 1.0;
        //    if (ResourceRequestList == null)
        //        return proportion;

        //    if (resourceType == typeof(LabourType))
        //        return LabourLimitProportion;

        //    double totalNeeded = ResourceRequestList.Where(a => a.ResourceType == resourceType).Sum(a => a.Required);

        //    foreach (ResourceRequest item in ResourceRequestList.Where(a => a.ResourceType == resourceType))
        //    {
        //        if (resourceType == typeof(LabourType))
        //        {
        //            if (item.FilterDetails != null && ((item.FilterDetails.First() as LabourGroup).Parent as LabourRequirement).OnPartialResourcesAvailableAction == OnPartialResourcesAvailableActionTypes.UseAvailableWithImplications)
        //                proportion *= item.Provided / item.Required;
        //        }
        //        else // all other types
        //            proportion = item.Provided / item.Required;
        //    }
        //    return proportion;
        //}

        /// <summary>
        /// Method to determine if activity limited based on labour shortfall has been set
        /// </summary>
        /// <returns></returns>
        public bool IsLabourLimitSet
        {
            get
            {
                return FindAllChildren<LabourRequirement>().Where(a => a.OnPartialResourcesAvailableAction == OnPartialResourcesAvailableActionTypes.UseAvailableWithImplications).Any();
            }
        }

        /// <summary>
        /// Determine resources available and perform transmutation if needed.
        /// </summary>
        /// <param name="resourceRequests">List of requests</param>
        /// <param name="uniqueActivityID">Unique id for the activity</param>
        public bool CheckResources(IEnumerable<ResourceRequest> resourceRequests, Guid uniqueActivityID)
        {
            if (resourceRequests is null || !resourceRequests.Any())
            {
                // nothing to do
                this.Status = ActivityStatus.NotNeeded;
                return true;
            }

            foreach (ResourceRequest request in resourceRequests)
            {
                request.ActivityID = uniqueActivityID;
                request.Available = 0;

                // If resource group does not exist then provide required.
                // This means when resource is not added to model it will not limit simulations
                if (request.ResourceType == null || Resources.FindResource(request.ResourceType) == null)
                {
                    request.Available = request.Required;
                    request.Provided = request.Required;
                }
                else
                {
                    if (request.ResourceType == typeof(Labour))
                        // get available labour based on rules nad filter groups
                        request.Available = TakeLabour(request, false, this, Resources, (request.ActivityModel as IReportPartialResourceAction).AllowsPartialResourcesAvailable);
                    else
                        request.Available = TakeNonLabour(request, false);
                }
            }

            // for testing. report any requests with no activity model provided 
            if (resourceRequests.Where(a => a.ActivityModel is null).Any())
                throw new NotImplementedException($"Unknown ActivityModel in ResourceRequest for [{NameWithParent}]{Environment.NewLine}Code based error: contact developers");

            // report any requests with a non CLEMactivityBase activity model provided
            var wrongType = resourceRequests.Where(a => (a.ActivityModel is IReportPartialResourceAction) == false);
            if (wrongType.Any())
                throw new NotImplementedException($"Unsupported ActivityModel type [{string.Join("]&[", wrongType.Select(a => a.ActivityModel.GetType().ToString()))}] in ResourceRequest for [{NameWithParent}]{Environment.NewLine}Code based error: contact developers");

            // get requests needing transmutaion - must be flagged UseResourcesAvailable
            //IEnumerable<ResourceRequest> shortfallsToTransmute = resourceRequests.Where(a => MathUtilities.IsNegative(a.Available - a.Required) ? (a.ActivityModel as IReportPartialResourceAction).AllowsPartialResourcesAvailable : false);
            IEnumerable<ResourceRequest> shortfallsToTransmute = resourceRequests.Where(a => MathUtilities.IsNegative(a.Available - a.Required));
            if (shortfallsToTransmute.Any())
                // check what transmutations can occur
                Resources.TransmutateShortfall(shortfallsToTransmute);

            // if still in shortfall after attempting to to transmute and ShortfallAction is skipped set ability to transmute to false
            foreach (var skipped in shortfallsToTransmute.Select(a => a.ActivityModel).OfType<IReportPartialResourceAction>().Where(a => a.OnPartialResourcesAvailableAction == OnPartialResourcesAvailableActionTypes.SkipActivity))
                skipped.Status = ActivityStatus.Skipped;

            // check if need to do transmutations
            int countTransmutationsSuccessful = shortfallsToTransmute.Where(a => a.TransmutationPossible == true && a.AllowTransmutation).Count();
            bool allTransmutationsSuccessful = (shortfallsToTransmute.Where(a => a.TransmutationPossible == false && a.AllowTransmutation).Count() == 0);

            // if error and stop on shortfall and shortfalls still occur after any transmutation
            // racalc shortfalls ignoring any components with skip action
            shortfallsToTransmute = resourceRequests.Where(a => MathUtilities.IsNegative(a.Available - a.Required) && (a.ActivityModel as IReportPartialResourceAction).Status != ActivityStatus.Skipped);
            if (shortfallsToTransmute.Any())
            {
                if (!allTransmutationsSuccessful)
                {
                    if (OnPartialResourcesAvailableAction == OnPartialResourcesAvailableActionTypes.ReportErrorAndStop)
                    {
                        string errorMessage = $"Insufficient resources for [a={this.NameWithParent}] with [Report error and stop] selected as action when shortfall of resources for the activity";
                        Status = ActivityStatus.Critical;
                        throw new ApsimXException(this, errorMessage);
                    }
                    if (OnPartialResourcesAvailableAction == OnPartialResourcesAvailableActionTypes.SkipActivity)
                    {
                        Status = ActivityStatus.Skipped;
                    }
                }
                // if we get to this point we have handled situation where shortfalls occurred and skip or error set as OnPartialResourceAction
                // OR at least one transmutation successful and PerformWithPartialResources
                if (Status != ActivityStatus.Skipped)
                {
                    // do transmutations.
                    // this uses the current zone resources, but will find markets if needed in the process
                    Resources.TransmutateShortfall(shortfallsToTransmute, false);

                    // recheck resource amounts now that resources have been topped up
                    foreach (ResourceRequest request in resourceRequests)
                    {
                        // get resource
                        if (request.Resource != null)
                        {
                            request.Available = 0;
                            // get amount available
                            request.Available = Math.Min(request.Resource.Amount, request.Required);
                        }
                    }
                }
            }

            // false if skipped to ignore next section and take resources or perform any tasks
            return (Status != ActivityStatus.Skipped);
        }

        /// <summary>
        /// Report shortfalls and provide status to undertake tasks
        /// </summary>
        /// <param name="resourceRequests">List of requests</param>
        /// <param name="uniqueActivityID">Unique id for the activity</param>
        public bool ReportShortfalls(IEnumerable<ResourceRequest> resourceRequests, Guid uniqueActivityID)
        {
            bool componentError = false;
            // report any resource defecits here including if activity is skip of shortfall
            foreach (var item in resourceRequests.Where(a => MathUtilities.IsPositive(a.Required - a.Available)))
            {
                if ((item.ActivityModel as IReportPartialResourceAction).OnPartialResourcesAvailableAction == OnPartialResourcesAvailableActionTypes.ReportErrorAndStop)
                {
                    string warn = $"Insufficient [r={item.ResourceType.Name}] from [a={item.ActivityModel.Name}] {((item.ActivityModel != this) ? $" in [a={NameWithParent}]" : "")}{Environment.NewLine}[Report error and stop] is selected as action when shortfall of resources. Ensure sufficient resources are available or change OnPartialResourcesAvailableAction setting";
                    (item.ActivityModel as IReportPartialResourceAction).Status = ActivityStatus.Critical;
                    Warnings.CheckAndWrite(warn, Summary, this, MessageType.Error);
                    componentError = true;
                }

                ResourceRequestEventArgs rrEventArgs = new ResourceRequestEventArgs() { Request = item };

                if (item.Resource != null && (item.Resource as Model).FindAncestor<Market>() != null)
                {
                    ActivitiesHolder marketActivities = Resources.FoundMarket.FindChild<ActivitiesHolder>();
                    if (marketActivities != null)
                        marketActivities.ReportActivityShortfall(rrEventArgs);
                }
                else
                    ActivitiesHolder.ReportActivityShortfall(rrEventArgs);

                if (Status != ActivityStatus.Skipped && (item.ActivityModel as IReportPartialResourceAction).OnPartialResourcesAvailableAction != OnPartialResourcesAvailableActionTypes.SkipActivity)
                {
                    Status = ActivityStatus.Partial;
                    //deficitFound = true;
                }
            }
            if (componentError)
            {
                string errorMessage = $"Insufficient resources for components of [a={this.NameWithParent}] with [Report error and stop] selected as action when shortfall of resources.{Environment.NewLine}See CLEM component Messages for details of all resource shortfalls";
                Status = ActivityStatus.Critical;
                throw new ApsimXException(this, errorMessage);
            }

            //if (!deficitFound && Status != ActivityStatus.Skipped)
            //    Status = ActivityStatus.Success;

            return (Status == ActivityStatus.Skipped);
        }

        /// <summary>
        /// Try to take the Resources based on Resource Request List provided.
        /// Returns true if it was able to take the resources it needed.
        /// Returns false if it was unable to take the resources it needed.
        /// </summary>
        /// <param name="resourceRequestList"></param>
        /// <param name="triggerActivityPerformed"></param>
        public bool TakeResources(List<ResourceRequest> resourceRequestList, bool triggerActivityPerformed)
        {
            // no resources required or this is an Activity folder.
            if ((resourceRequestList == null)||(!resourceRequestList.Any()))
                return false;

            // remove activity resources 
            // remove all shortfall requests marked as skip
            resourceRequestList.RemoveAll(a => MathUtilities.IsNegative(a.Available - a.Required) && (a.ActivityModel as IReportPartialResourceAction).AllowsPartialResourcesAvailable == false);
            var requestsToWorkWith = resourceRequestList;

            if (requestsToWorkWith.Any())
            {
                // all ok with this activity partial resources setting and all identifiable components.
                foreach (ResourceRequest request in requestsToWorkWith)
                {
                    // get resource
                    request.Provided = 0;
                    // do not take if the resource does not exist
                    if (request.ResourceType != null && Resources.FindResource(request.ResourceType) != null)
                    { 
                        if (request.ResourceType == typeof(Labour))
                            // get available labour based on rules.
                            request.Available = TakeLabour(request, true, this, Resources, (request.ActivityModel as IReportPartialResourceAction).AllowsPartialResourcesAvailable);
                        else
                            request.Available = TakeNonLabour(request, true);
                    }
                }
            }
            //else
            //    Status = ActivityStatus.Ignored;

            return Status != ActivityStatus.Ignored;
        }

        /// <summary>
        /// Method to determine available labour based on filters and take it if requested.
        /// </summary>
        /// <param name="request">Resource request details</param>
        /// <param name="removeFromResource">Determines if only calculating available labour or labour removed</param>
        /// <param name="callingModel">Model calling this method</param>
        /// <param name="resourceHolder">Location of resource holder</param>
        /// <param name="allowPartialAction">Does this activity support partial action on resource shortfall</param>
        /// <returns></returns>
        public static double TakeLabour(ResourceRequest request, bool removeFromResource, CLEMModel callingModel, ResourcesHolder resourceHolder, bool allowPartialAction)
        {
            double amountProvided = 0;
            double amountNeeded = request.Required;
            LabourGroup current = request.FilterDetails.OfType<LabourGroup>().FirstOrDefault();

            LabourRequirement lr;
            if (current != null)
            {
                if (current.Parent is LabourRequirement)
                    lr = current.Parent as LabourRequirement;
                else
                    // coming from Transmutation request
                    lr = new LabourRequirement()
                    {
                        LimitStyle = LabourLimitType.AsDaysRequired,
                        ApplyToAll = false,
                        MaximumPerGroup = 10000,
                        MaximumPerPerson = 1000,
                        MinimumPerPerson = 0
                    };
            }
            else
                lr = callingModel.FindAllChildren<LabourRequirement>().FirstOrDefault();

            lr.CalculateLimits(amountNeeded);
            amountNeeded = Math.Min(amountNeeded, lr.MaximumDaysPerGroup);
            request.Required = amountNeeded;
            // may need to reduce request here or shortfalls will be triggered

            int currentIndex = 0;
            if (current == null)
                // no filtergroup provided so assume any labour
                current = new LabourGroup();

            request.ResourceTypeName = "Labour";
            ResourceRequest removeRequest = new ResourceRequest()
            {
                ActivityID = request.ActivityID,
                ActivityModel = request.ActivityModel,
                AdditionalDetails = request.AdditionalDetails,
                AllowTransmutation = request.AllowTransmutation,
                Available = request.Available,
                FilterDetails = request.FilterDetails,
                Provided = request.Provided,
                Category = request.Category,
                RelatesToResource = request.RelatesToResource,
                Required = request.Required,
                Resource = request.Resource,
                ResourceType = request.ResourceType,
                ResourceTypeName = (request.Resource is null ? "" : (request.Resource as CLEMModel).NameWithParent)
            };

            // start with top most LabourFilterGroup
            while (current != null && amountProvided < amountNeeded)
            {
                IEnumerable<LabourType> items = resourceHolder.FindResource<Labour>().Items;
                items = items.Where(a => (a.LastActivityRequestID != callingModel.UniqueID) || (a.LastActivityRequestID == callingModel.UniqueID && a.LastActivityRequestAmount < lr.MaximumDaysPerPerson));
                items = current.Filter(items);

                // search for people who can do whole task first
                while (amountProvided < amountNeeded && items.Where(a => a.LabourCurrentlyAvailableForActivity(callingModel.UniqueID, lr.MaximumDaysPerPerson) >= request.Required).Any())
                {
                    // get labour least available but with the amount needed
                    LabourType lt = items.Where(a => a.LabourCurrentlyAvailableForActivity(callingModel.UniqueID, lr.MaximumDaysPerPerson) >= request.Required).OrderBy(a => a.LabourCurrentlyAvailableForActivity(callingModel.UniqueID, lr.MaximumDaysPerPerson)).FirstOrDefault();

                    double amount = Math.Min(amountNeeded - amountProvided, lt.LabourCurrentlyAvailableForActivity(callingModel.UniqueID, lr.MaximumDaysPerPerson));

                    // limit to max allowed per person
                    amount = Math.Min(amount, lr.MaximumDaysPerPerson);
                    // limit to min per person to do activity
                    if (amount < lr.MinimumPerPerson)
                    {
                        request.Category = "Min labour limit";
                        return amountProvided;
                    }

                    amountProvided += amount;
                    removeRequest.Required = amount;
                    if (removeFromResource)
                    {
                        lt.LastActivityRequestID = callingModel.UniqueID;
                        lt.LastActivityRequestAmount = amount;
                        lt.Remove(removeRequest);
                        request.Provided += removeRequest.Provided;
                        request.Value += request.Provided * lt.PayRate();
                    }
                }

                // if still needed and allow partial resource use.
                if (allowPartialAction)
                {
                    if (amountProvided < amountNeeded)
                    {
                        // then search for those that meet criteria and can do part of task
                        foreach (LabourType item in items.Where(a => a.LabourCurrentlyAvailableForActivity(callingModel.UniqueID, lr.MaximumDaysPerPerson) >= 0).OrderByDescending(a => a.LabourCurrentlyAvailableForActivity(callingModel.UniqueID, lr.MaximumDaysPerPerson)))
                        {
                            if (amountProvided >= amountNeeded)
                                break;

                            double amount = Math.Min(amountNeeded - amountProvided, item.LabourCurrentlyAvailableForActivity(callingModel.UniqueID, lr.MaximumDaysPerPerson));

                            // limit to max allowed per person
                            amount = Math.Min(amount, lr.MaximumDaysPerPerson);

                            // limit to min per person to do activity
                            if (amount >= lr.MinimumDaysPerPerson)
                            {
                                amountProvided += amount;
                                removeRequest.Required = amount;
                                if (removeFromResource)
                                {
                                    if (item.LastActivityRequestID != callingModel.UniqueID)
                                        item.LastActivityRequestAmount = 0;
                                    item.LastActivityRequestID = callingModel.UniqueID;
                                    item.LastActivityRequestAmount += amount;
                                    item.Remove(removeRequest);
                                    request.Provided += removeRequest.Provided;
                                    request.Value += request.Provided * item.PayRate();
                                }
                            }
                            else
                                currentIndex = request.FilterDetails.Count;
                        }
                    }
                }
                currentIndex++;
                var currentFilterGroups = current.FindAllChildren<LabourGroup>();
                if (currentFilterGroups.Any())
                    current = currentFilterGroups.FirstOrDefault();
                else
                    current = null;
            }
            // report amount gained.
            return amountProvided;
        }

        /// <summary>
        /// Method to determine available non-labour resources and take if requested.
        /// </summary>
        /// <param name="request">Resource request details</param>
        /// <param name="removeFromResource">Determines if only calculating available labour or labour removed</param>
        /// <returns></returns>
        private double TakeNonLabour(ResourceRequest request, bool removeFromResource)
        {
            // get available resource
            if (request.Resource == null)
                //If it hasn't been assigned try and find it now.
                request.Resource = Resources.FindResourceType<ResourceBaseWithTransactions, IResourceType>(request, OnMissingResourceActionTypes.Ignore, OnMissingResourceActionTypes.Ignore);

            if (request.Resource != null)
                // get amount available
                request.Available = Math.Min(request.Resource.Amount, request.Required);

            if (removeFromResource && request.Resource != null)
                request.Resource.Remove(request);

            return request.Available;
        }

        /// <summary>
        /// Method to trigger an Activity Performed event 
        /// </summary>
        public void TriggerOnActivityPerformed()
        {
            ActivitiesHolder?.ReportActivityPerformed(new ActivityPerformedEventArgs
            {
                Name = this.Name,
                Status = this.Status,
                Id = this.UniqueID.ToString(),
            });
        }

        /// <summary>
        /// Method to trigger an Activity Performed event 
        /// </summary>
        /// <param name="status">The status of this activity to be reported</param>
        public void TriggerOnActivityPerformed(ActivityStatus status)
        {
            this.Status = status;
            ActivitiesHolder?.ReportActivityPerformed(new ActivityPerformedEventArgs
            {
                Name = this.Name,
                Status = status,
                Id = this.UniqueID.ToString(),
            });
        }
    }
}
