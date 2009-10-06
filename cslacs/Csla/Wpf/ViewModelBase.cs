﻿using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Reflection;
using Csla.Reflection;

#if SILVERLIGHT
using Csla.Core;

namespace Csla.Silverlight
#else
namespace Csla.Wpf
#endif
{
  /// <summary>
  /// Base class used to create ViewModel objects that
  /// implement their own commands/verbs/actions.
  /// </summary>
  /// <typeparam name="T">Type of the Model object.</typeparam>
#if SILVERLIGHT
  public abstract class ViewModelBase<T> : FrameworkElement,
    INotifyPropertyChanged
#else
  public abstract class ViewModelBase<T> : DependencyObject,
    INotifyPropertyChanged
#endif
  {
    #region Properties

    /// <summary>
    /// Gets or sets the Model object.
    /// </summary>
    public static readonly DependencyProperty ModelProperty =
        DependencyProperty.Register("Model", typeof(object), typeof(ViewModelBase<T>), null);
    /// <summary>
    /// Gets or sets the Model object.
    /// </summary>
    public object Model
    {
      get { return GetValue(ModelProperty); }
      set { SetValue(ModelProperty, value); }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the
    /// ViewModel should automatically managed the
    /// lifetime of the Model.
    /// </summary>
    public static readonly DependencyProperty ManageObjectLifetimeProperty =
        DependencyProperty.Register("ManageObjectLifetime", typeof(bool),
        typeof(ViewModelBase<T>), new PropertyMetadata(true));
    /// <summary>
    /// Gets or sets a value indicating whether the
    /// ViewManageObjectLifetime should automatically managed the
    /// lifetime of the ManageObjectLifetime.
    /// </summary>
    public bool ManageObjectLifetime
    {
      get { return (bool)GetValue(ManageObjectLifetimeProperty); }
      set { SetValue(ManageObjectLifetimeProperty, value); }
    }

    private Exception _error;

    /// <summary>
    /// Gets the Error object corresponding to the
    /// last asyncronous operation.
    /// </summary>
    public Exception Error
    {
      get { return _error; }
      protected set
      {
        _error = value;
        OnPropertyChanged("Error");
      }
    }

    private bool _isBusy;

    /// <summary>
    /// Gets a value indicating whether this object is
    /// executing an asynchronous process.
    /// </summary>
    public bool IsBusy
    {
      get { return _isBusy; }
      protected set
      {
        _isBusy = value;
        OnPropertyChanged("IsBusy");
        SetProperties();
      }
    }

    #endregion

    #region Can___ properties

    private bool _canSave;
    private bool _canCancel;

    /// <summary>
    /// Gets a value indicating whether the Model can be saved.
    /// </summary>
    public virtual bool CanSave { get { return _canSave; } }
    /// <summary>
    /// Gets a value indicating whether the Model can be canceled.
    /// </summary>
    public virtual bool CanCancel { get { return _canCancel; } }
    /// <summary>
    /// Gets a value indicating whether a new item can be
    /// added to the Model (if it is a collection).
    /// </summary>
    public virtual bool CanAddNew { get { return Model != null && Model is IBindingList; } }
    /// <summary>
    /// Gets a value indicating whether an item can be
    /// removed from the Model (if it is a collection).
    /// </summary>
    public virtual bool CanRemove { get { return Model != null && Model is System.Collections.IList; } }
    /// <summary>
    /// Gets a value indicating whether the Model can be
    /// marked for deletion (if it is an editable root object).
    /// </summary>
    public virtual bool CanDelete { get { return Model != null && Model is Csla.Core.IEditableBusinessObject; } }

    private void SetProperties()
    {
      bool value;
      value = GetCanSave();
      if (_canSave != value)
      {
        _canSave = value;
        OnPropertyChanged("CanSave");
      }
      value = GetCanCancel();
      if (_canCancel != value)
      {
        _canCancel = value;
        OnPropertyChanged("CanCancel");
      }
    }

    private bool GetCanSave()
    {
      if (Model == null) return false;
      var track = Model as Csla.Core.ITrackStatus;
      if (track != null)
        return track.IsSavable;
      return false;
    }

    private bool GetCanCancel()
    {
      if (!this.ManageObjectLifetime) return false;
      if (Model == null) return false;
      var undo = Model as Csla.Core.ISupportUndo;
      if (undo == null)
        return false;
      var track = Model as Csla.Core.ITrackStatus;
      if (track != null)
        return track.IsDirty;
      return false;
    }

    #endregion

    #region Verbs

    /// <summary>
    /// Creates or retrieves a new instance of the 
    /// Model by invoking a static factory method.
    /// </summary>
    /// <param name="factoryMethod">Name of the static factory method.</param>
    /// <param name="factoryParameters">Factory method parameters.</param>
    protected virtual void DoRefresh(string factoryMethod, params object[] factoryParameters)
    {
      if (typeof(T) != null)
        try
        {
          Error = null;
          this.IsBusy = true;
          var parameters = new List<object>(factoryParameters);
          parameters.Add(CreateHandler(typeof(T)));

          MethodCaller.CallFactoryMethod(typeof(T), factoryMethod, parameters.ToArray());
        }
        catch (Exception ex)
        {
          this.Error = ex;
        }
    }

    private Delegate CreateHandler(Type objectType)
    {
      var args = typeof(DataPortalResult<>).MakeGenericType(objectType);
      MethodInfo method = MethodCaller.GetNonPublicMethod(this.GetType(), "QueryCompleted");
      Delegate handler = Delegate.CreateDelegate(typeof(EventHandler<>).MakeGenericType(args), this, method);
      return handler;
    }


    private void QueryCompleted(object sender, EventArgs e)
    {
      this.IsBusy = false;
      var eventArgs = (IDataPortalResult)e;
      if (eventArgs.Error == null)
      {
        Model = eventArgs.Object;
        if (this.ManageObjectLifetime)
        {
          var undo = Model as Csla.Core.ISupportUndo;
          if (undo != null)
            undo.BeginEdit();
        }
        SetProperties();
      }
      else
      {
        Error = eventArgs.Error;
      }
    }

    /// <summary>
    /// Saves the Model, first committing changes
    /// if ManagedObjectLifetime is true.
    /// </summary>
    protected virtual void DoSave()
    {
      Csla.Core.ISupportUndo undo;
      if (this.ManageObjectLifetime)
      {
        undo = Model as Csla.Core.ISupportUndo;
        if (undo != null)
          undo.ApplyEdit();
      }

      var savable = (Csla.Core.ISavable)Model;
      savable.Saved += (o, e) =>
      {
        IsBusy = false;
        if (e.Error == null)
        {
          var result = e.NewObject;
          if (this.ManageObjectLifetime)
          {
            undo = result as Csla.Core.ISupportUndo;
            if (undo != null)
              undo.BeginEdit();
          }
          Model = (T)result;
        }
        else
        {
          if (this.ManageObjectLifetime)
          {
            undo = Model as Csla.Core.ISupportUndo;
            if (undo != null)
              undo.BeginEdit();
          }
          Error = e.Error;
        }
        OnSaved();
      };
      Error = null;
      IsBusy = true;
      savable.BeginSave();
    }

    /// <summary>
    /// Method called after a save operation 
    /// has completed (whether successful or
    /// not).
    /// </summary>
    protected virtual void OnSaved()
    { }

    /// <summary>
    /// Cancels changes made to the model 
    /// if ManagedObjectLifetime is true.
    /// </summary>
    protected virtual void DoCancel()
    {
      if (this.ManageObjectLifetime)
      {
        var undo = Model as Csla.Core.ISupportUndo;
        if (undo != null)
          undo.CancelEdit();
      }
    }

    /// <summary>
    /// Adds a new item to the Model (if it
    /// is a collection).
    /// </summary>
    protected virtual void DoAddNew()
    {
      ((IBindingList)Model).AddNew();
    }

    /// <summary>
    /// Removes an item from the Model (if it
    /// is a collection).
    /// </summary>
    protected virtual void DoRemove(T item)
    {
      ((System.Collections.IList)Model).Remove(item);
    }

    /// <summary>
    /// Marks the Model for deletion (if it is an
    /// editable root object).
    /// </summary>
    protected virtual void DoDelete()
    {
      ((Csla.Core.IEditableBusinessObject)Model).Delete();
    }

    #endregion

    #region INotifyPropertyChanged Members

    /// <summary>
    /// Event raised when a property changes.
    /// </summary>
    public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;

    /// <summary>
    /// Raise the PropertyChanged event.
    /// </summary>
    /// <param name="propertyName">Name of the changed property.</param>
    protected virtual void OnPropertyChanged(string propertyName)
    {
      if (PropertyChanged != null)
        PropertyChanged(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
    }

    #endregion
  }
}
