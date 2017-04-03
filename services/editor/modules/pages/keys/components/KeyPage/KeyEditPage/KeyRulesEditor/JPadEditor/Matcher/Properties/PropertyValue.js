import React from 'react';
import style from './styles.css';
import { WithContext as ReactTags } from 'react-tag-input';
import R from 'ramda';
import ComboBox from '../../../../../../../../../components/common/ComboBox/ComboBox';
import { inOp } from '../../../../../../../../../services/operators-provider';

const TagsPropertyValue = ({ onUpdate, value, suggestions }) => {
  let indexedTags = value.map(x => ({ id: x, text: x }));

  const handleAddtion = (newValue) => onUpdate([...value, newValue]);
  const handleDelete = (valueIndex) => onUpdate(R.remove(valueIndex, 1, value));
  const indexedSuggestions = suggestions ? suggestions.map(x => x) : [];

  return (
    <div className={style['tags-wrapper']}>
      <ReactTags tags={indexedTags}
        suggestions={indexedSuggestions}
        handleAddition={handleAddtion}
        handleDelete={handleDelete}
        placeholder="Add value"
        minQueryLength={1}
        allowDeleteFromEmptyInput={true}
        classNames={{
          tags: style['tags-container'],
          tagInput: style['tag-input'],
          tag: style['tag'],
          remove: style['tag-delete-button'],
          suggestions: style['tags-suggestion'],
        }}
      />
    </div>
  );
};

const InputPropertyValue = ({ onUpdate, value, placeholder='Value' }) => (
  <input className={style['value-input']}
    type="text"
    onChange={(e) => onUpdate(e.target.value)}
    value={value}
    placeholder={placeholder}
  />
);

function PropertyValueComponent({ onUpdate, propertyTypeDetails, value, selectedOperator, placeholder='Value' }) {
  let allowedValues = propertyTypeDetails.allowedValues || [];

  if (selectedOperator === inOp.operatorValue)
    return (
      <TagsPropertyValue
        onUpdate={onUpdate}
        value={value}
        suggestions={allowedValues}
      />
    );

  if (allowedValues.length > 0)
    return (
      <ComboBox
        options={allowedValues}
        placeholder={placeholder}
        wrapperThemeClass={style['property-value-combo-box']}
        onChange={(selectedValue) => {
          onUpdate(selectedValue);
        }}
        selected={allowedValues.filter(x => x.toLowerCase() == (value || "").toLowerCase())}
      />
    );

  return (
    <InputPropertyValue {...{onUpdate, value, placeholder}} />
  );
}

export default (props) => <div className={style['property-value-wrapper']}>
  <PropertyValueComponent {...props} />
</div>;